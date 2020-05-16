using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GoogleDriveTools
{
    public class GoogleDataUploader : MonoBehaviour
    {
        //List of datafiles that first show all files, then remaining files to be uploaded
        private static List<string> filePathsToUpload = new List<string>();

        //Webrequest object that can be requested for any web requests.
        private static bool obtainedValidVersionFolder = false;
        private static UnityWebRequest webRequestObj = null;

        //Google API location contants
        private const string googleApiBaseUrl = "https://www.googleapis.com/drive/v3/";
        private const string googleUpdateFilePostUrl = "https://www.googleapis.com/upload/drive/v3/files/";
        private const string googleUploadFilePostUrl = "https://www.googleapis.com/upload/drive/v3/files?supportsTeamDrives=true";
        private const string googleUploadFolderPostUrl = "https://www.googleapis.com/drive/v3/files?supportsTeamDrives=true";

        //Google API usage constants
        private const string googleTargetDriveId = "files?corpora=drive&driveId=[DRIVEID]&includeItemsFromAllDrives=true";
        private const string googleUploadMineFolderAttributes = "&q=mimeType%20%3D%20'application%2Fvnd.google-apps.folder'%20and%20name%20%3D%20";
        private const string googleCheckFileAttributes = "&q=mimeType%20!%3D%20'application%2Fvnd.google-apps.folder'%20and%20name%20%3D%20";
        private const string googleAllowTeamDrive = "&supportsAllDrives=true&supportsTeamDrives=true";

        //Google API Access Token and Target Drive
        private static string googleDriveAccessToken = "";
        private static System.DateTime lastTokenReceived;
        private static string googleVersionFolderId = "";
        private const string googleRootFolderId = "[DRIVEROOTID]";

        //String manipulation constants
        private const int googleExistingFolderCutOffIndex = 102;
        private const int googlenewFolderCutOffIndex = 31;

        //Upload process related string storage
        private static string uploaderHashId = "";
        private static string currentUnescapedFileName = "";
        private static string currentEscapedFileName = "";
        private static string currentFolderId = "";
        private static string currentFileId = "";
        private static string currentUploadLocationId = "";

        private bool obtainedValidAccessKey = false;
        private bool lastResponseValid = false;
        private bool levelFolderExistOnDrive = false;
        private bool levelFileExistsOnDrive = false;

        //Callbacks
        static private System.Action onCompleted;

        static private GoogleDataUploader instance;

        enum GoogleAttributeType { Folder, File};
        enum GoogleUploadType { Create, Update };

        public void Awake()
        {
            instance = this;
        }

        public static void CheckIfVersionFolderExists()
        {
            if (googleVersionFolderId.Length == 0 && obtainedValidVersionFolder == false)
            {
                instance.StartCoroutine("CheckIfFolderVersionExistsOnDrive"); //If any key is found, the functionality continues at CheckForGoogleAccessToken
            }
        }

        // Static entry point for other classes to upload a file to google drive. Returns true if process started successfully, false if anything was wrong.
        public static bool UploadLocalFileToGoogleDrive(string filepath, System.Action onCompleted = null)
        {
            GoogleDataUploader.onCompleted = onCompleted;
            if (instance == null)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to upload as no instance was found.");
                onCompleted?.Invoke();
                return false;
            }
            if (!obtainedValidVersionFolder)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to upload {filepath} as no version folder was set!");
            }
            if (!CheckIfFileIsValid(filepath))
            {
                Debug.LogError($"[GoogleDataUploader] Unable to upload {filepath}.");
                onCompleted?.Invoke();
                return false;
            }
            //Get hashing id
            CheckForOrGenerateUploadHashId();
            if (uploaderHashId.Length <= 0)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to retrieve or generate unique upload hash id.");
                onCompleted?.Invoke();
                return false;
            }

            instance.StartCoroutine("ParseQueuedUploadFiles"); //If any key is found, the functionality continues at CheckForGoogleAccessToken
            return true;
        }


        private static bool CheckIfFileIsValid(string filePathToCheck)
        {
            bool validFilePath = System.IO.File.Exists(filePathToCheck);
            if (!validFilePath)
            {
                Debug.Log($"[GoogleDataUploader] Unable to upload {filePathToCheck} as a valid file was not found.");
                return false;
            }

            //As it's a binary file without extension, do this check. Otherwise replace with desired extension format in future.
            if (Path.GetExtension(filePathToCheck) != string.Empty)
            {
                Debug.Log($"[GoogleDataUploader] {filePathToCheck} does not meet the file requirements to be uploaded.");
                return false;
            }
            filePathsToUpload.Add(filePathToCheck);
            return true;
        }

        private static void CheckForOrGenerateUploadHashId()
        {
            if (File.Exists(Application.persistentDataPath + "/PlayerUploadId.hash"))
            {
                FileStream stream = new FileStream(Application.persistentDataPath + "/PlayerUploadId.hash", FileMode.Open);
                StreamReader reader = new StreamReader(stream);
                if (reader != null)
                {
                    uploaderHashId = reader.ReadToEnd();
                    reader.Close();
                    stream.Close();
                }
            }
            else
            {
                string newHash = GenerateNewHashId();

                FileStream stream = new FileStream(Application.persistentDataPath + "/PlayerUploadId.hash", FileMode.Create);
                StreamWriter writer = new StreamWriter(stream);
                if(writer != null)
                {
                    writer.Write(newHash);
                    writer.Close();
                    stream.Close();
                    uploaderHashId = newHash;
                }
            }
        }

        private static string GenerateNewHashId()
        {
            return System.Guid.NewGuid().ToString();
        }

        private IEnumerator CheckIfFolderVersionExistsOnDrive()
        {
            yield return RequestNewGoogleDriveAccessToken();
            if (!obtainedValidAccessKey)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to upload files as no valid access key was given.");
                yield break;
            }

            //Check if version folder exists on drive;
            string versionName = ReplaceEscapeCharacters(Application.version);
            versionName = Regex.Replace(versionName, " ", "_");
            yield return CheckIfGoogleDriveItemExist(versionName, GoogleAttributeType.Folder);
            if (!lastResponseValid)
            {
                yield break;//continue;
            }

            if (!levelFolderExistOnDrive)
            {
                yield return CreateMissingGoogleDriveFolder(versionName, googleRootFolderId);
            }

            if (currentFolderId.Length <= 0)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to upload files to the right version as the version folder couldn't be created/found.");
            }
            else
            {
                googleVersionFolderId = currentFolderId;
                obtainedValidVersionFolder = true;
                Debug.Log($"[GoogleDataUploader] Created or found a valid folder with the correct version to upload towards.");
            }
        }

        private IEnumerator ParseQueuedUploadFiles()
        {
            yield return RequestNewGoogleDriveAccessToken();
            if (!obtainedValidAccessKey)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to upload files as no valid access key was given.");
                yield break;
            }
            
            //A valid access key has been retrieved, start going through the upload queue.
            //Some files contain characters that aren't allowed on Google Drive without a big headache, hold the unescaped name for later use.
            currentUnescapedFileName = Path.GetFileNameWithoutExtension(filePathsToUpload[0]);
            currentEscapedFileName = ReplaceEscapeCharacters(currentUnescapedFileName);

            //Check for level folder
            yield return CheckIfGoogleDriveItemExist(currentEscapedFileName, GoogleAttributeType.Folder, googleVersionFolderId);

            //Check if CheckIfGoogleDriveFolderExist went alright
            if (!lastResponseValid)
            {
                yield break;//continue;
            }

            //Create folder on drive before continuing or immediately attempt to upload the file
            if (!levelFolderExistOnDrive)
            {
                yield return CreateMissingGoogleDriveFolder(currentEscapedFileName, googleVersionFolderId);
            }

            //Check if InitiateResumableUploadSession went right
            if (!lastResponseValid)
            {
                yield break;
            }

            //Check if current file exists in the drive already
            yield return CheckIfGoogleDriveItemExist($"{currentEscapedFileName}_Hash{uploaderHashId}", GoogleAttributeType.File, currentFolderId);

            //Check if CheckIfGoogleDriveItemExist went right
            if (!lastResponseValid)
            {
                yield break;
            }

            //Create a new file or update existing based on previous result
            if (!levelFileExistsOnDrive)
            {
                yield return InitiateResumableUploadSession(currentEscapedFileName, currentFolderId, GoogleUploadType.Create);
            }
            else
            {
                yield return InitiateResumableUploadSession(currentEscapedFileName, currentFileId, GoogleUploadType.Update);

                //Finally upload the file completely
            }

            if (!lastResponseValid)
            {
                yield break;
            }

            yield return UploadResumableGoogleFile(currentUploadLocationId);
            onCompleted?.Invoke();
            Debug.Log($"[GoogleDataUploader] Completed the upload routine.");
        }


        private IEnumerator RequestNewGoogleDriveAccessToken()
        {
            if (googleDriveAccessToken.Length > 0 && lastTokenReceived != null && (lastTokenReceived.AddMinutes(60) > System.DateTime.Now))
            {
                yield break;
            }

            WWWForm apiRequestForm = new WWWForm();
            apiRequestForm.AddField("refresh_token", "[REFRESH_TOKEN]");
            apiRequestForm.AddField("client_id", "[CLIENT_ID]");
            apiRequestForm.AddField("client_secret", "[CLIENT_SECRET]");
            apiRequestForm.AddField("grant_type", "refresh_token");

            obtainedValidAccessKey = false;
            webRequestObj = new UnityWebRequest();
            webRequestObj = UnityWebRequest.Post("https://oauth2.googleapis.com/token", apiRequestForm);
            yield return webRequestObj.SendWebRequest();

            if (webRequestObj.isHttpError || webRequestObj.responseCode != 200)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to get a new Google Drive access token. Try again or contact Tech/QA.");
                yield break;
            }
            lastTokenReceived = System.DateTime.Now;
            googleDriveAccessToken = ReplaceEscapeCharacters(webRequestObj.downloadHandler.text, true);

            //If access token is complete, go to new section
            Debug.Log($"[GoogleDataUploader] Received a valid Google API access token to upload files.");

            obtainedValidAccessKey = true;
        }


        //Regex ultility function to get the raw access code from the string
        public static string ReplaceEscapeCharacters(string rawString, bool accessToken = false)
        {
            string tokenString = "";
            if (!accessToken)
            {
                return Regex.Replace(rawString, @"\t|\n|\r|\'", "");
            }

            tokenString = Regex.Replace(rawString, @"\t |\n|\r|""", "");
            tokenString = tokenString.Replace('/', ' ');

            string retrieveAccessTokenRegex = @"access_token: ([^/,]+)";
            var splittedValue = Regex.Split(tokenString, retrieveAccessTokenRegex);
            return splittedValue[1];
        }


        private IEnumerator CheckIfGoogleDriveItemExist(string itemToCheck, GoogleAttributeType attributeType, string parentId = "")
        {
            string webUrl = $@"{googleApiBaseUrl}{googleTargetDriveId}{googleAllowTeamDrive}";
            if (attributeType == GoogleAttributeType.Folder)
            {
                levelFolderExistOnDrive = false;
                if (parentId.Length != 0)
                {
                    //%20and%20''%20in%20parents
                    webUrl += $"{googleUploadMineFolderAttributes}'{itemToCheck}'%20and%20'{parentId}'%20in%20parents%20and%20trashed%20!%3D%20true&access_token={googleDriveAccessToken}";
                }
                else
                {
                    webUrl += $"{googleUploadMineFolderAttributes}'{itemToCheck}'%20and%20trashed%20!%3D%20true&access_token={googleDriveAccessToken}";
                }
                
                Debug.Log($"[GoogleDataUploader] Checking if folder `{itemToCheck}` exist on Google Drive...");
            }
            else
            {
                levelFileExistsOnDrive = false;
                if (parentId.Length != 0)
                {
                    webUrl += $"{googleCheckFileAttributes}'{itemToCheck}'%20and%20%27{parentId}%27%20in%20parents%20and%20trashed%20!%3D%20true&access_token={googleDriveAccessToken}";
                }
                else
                {
                    webUrl += $"{googleCheckFileAttributes}'{itemToCheck}'%20and%20trashed%20!%3D%20true&access_token={googleDriveAccessToken}";
                }
                Debug.Log($"[GoogleDataUploader] Checking if file `{itemToCheck}` exist on Google Drive...");
            }
           

            webRequestObj = new UnityWebRequest();
            webRequestObj = UnityWebRequest.Get(webUrl);
            lastResponseValid = false;
            yield return webRequestObj.SendWebRequest();

            if (webRequestObj.isHttpError || webRequestObj.responseCode != 200)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to get a valid response on the Google Folder Check, removing current item.");
                if (filePathsToUpload.Count > 0) 
                { 
                    filePathsToUpload.RemoveAt(0); 
                }
                yield break;
            }
            string escapedResultFromJson = ReplaceEscapeCharacters(webRequestObj.downloadHandler.text);
            // cut string
            int lengthString = escapedResultFromJson.Length;
            if (escapedResultFromJson.Length < 75)
            {
                lastResponseValid = true;
                Debug.Log($"[GoogleDataUploader] Item {itemToCheck} does not exist yet.");
                yield break;
            }

            //Cut the string to obtain the Google Drive ID as that's faster than parsing it..
            int cutofCIndex = googleExistingFolderCutOffIndex;
            if (lengthString - cutofCIndex <= 0)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to get a valid response on the Google item check, removing current item.");
                if (filePathsToUpload.Count > 0) 
                {
                    filePathsToUpload.RemoveAt(0);    //Pop the first index;
                }
                yield break;
            }

            lastResponseValid = true;

            //Remove the google drive folder id from the escaped string
            string cutToId = escapedResultFromJson.Substring(cutofCIndex, lengthString - cutofCIndex);
            
            if (attributeType == GoogleAttributeType.Folder)
            {
                currentFolderId = cutToId.Substring(0, cutToId.IndexOf(",") - 1);

                Debug.Log("[GoogleDataUploader] Folder exists on Google Drive");
                levelFolderExistOnDrive = true;
            }
            else
            {
                currentFileId = cutToId.Substring(0, cutToId.IndexOf(",") - 1);

                Debug.Log("[GoogleDataUploader] File exists on Google Drive");
                levelFileExistsOnDrive = true;

            }

        }


        private IEnumerator CreateMissingGoogleDriveFolder(string nameOfFolder, string parentId)
        {
            //Build JsonString for GoogleDrive upload...
            string formDataString = $@"{{""name"": ""{nameOfFolder}"", ""mimeType"": ""application/vnd.google-apps.folder"", ""parents"": [ ""{parentId}""] }}";
            byte[] binaryRequest = Encoding.UTF8.GetBytes(formDataString);
            webRequestObj = new UnityWebRequest(googleUploadFolderPostUrl, "POST");

            webRequestObj.uploadHandler = new UploadHandlerRaw(binaryRequest);
            webRequestObj.downloadHandler = new DownloadHandlerBuffer();

            webRequestObj.SetRequestHeader("Content-Type", "application/json");
            webRequestObj.SetRequestHeader("Accept", "application/json");
            webRequestObj.SetRequestHeader("Authorization", $"Bearer {googleDriveAccessToken}");

            lastResponseValid = false;
            yield return webRequestObj.SendWebRequest();

            if (webRequestObj.isHttpError || webRequestObj.responseCode != 200)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to get a valid response on the Google Folder creation, removing current item.");
                filePathsToUpload.RemoveAt(0);    //Pop the first index;
                yield break;
            }

            string escapedResultFromJson = ReplaceEscapeCharacters(webRequestObj.downloadHandler.text);
            // cut string
            int lengthString = escapedResultFromJson.Length;
            if (escapedResultFromJson.Length < 75)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to deduce result form Google folder creation, removing current item.");
                filePathsToUpload.RemoveAt(0);    //Pop the first index;
                yield break;
            }

            //Cut the string as that's faster than parsing it..
            int cutofCIndex = googlenewFolderCutOffIndex;
            if (lengthString - cutofCIndex <= 0)
            {
                Debug.LogError($"[GoogleDataUploader] Unable to get a valid response on the Google Folder Check, removing current item.");
                filePathsToUpload.RemoveAt(0);    //Pop the first index;
                yield break;
            }

            lastResponseValid = true;

            string cutToId = escapedResultFromJson.Substring(cutofCIndex, lengthString - cutofCIndex);
            currentFolderId = cutToId.Substring(0, cutToId.IndexOf(",") - 1);
            levelFolderExistOnDrive = true;
            Debug.Log($"[GoogleDataUploader] Created folder {nameOfFolder} with id {currentFolderId} on Google Drive");
        }


        private IEnumerator InitiateResumableUploadSession(string nameOfFile, string itemId, GoogleUploadType googleUploadType)
        {
            string formDataString = "";
            if (googleUploadType == GoogleUploadType.Create)
            {
                formDataString = $@"{{""name"": ""{nameOfFile}_Hash{uploaderHashId}"", ""mimeType"": ""application/octet-stream"", ""parents"": [ ""{itemId}""] }}";
                webRequestObj = new UnityWebRequest($"{googleUploadFilePostUrl}&uploadType=resumable", "POST");
            }
            else
            {
                formDataString = $@"{{""name"": ""{nameOfFile}_Hash{uploaderHashId}"", ""mimeType"": ""application/octet-stream"" }}";
                webRequestObj = new UnityWebRequest($"{googleUpdateFilePostUrl}{itemId}?supportsTeamDrives=true&uploadType=resumable", "PATCH");
            }

            //Build JsonString for GoogleDrive upload...
            byte[] binaryRequest = Encoding.UTF8.GetBytes(formDataString);
            long fileSizeToUpload = GetRequestBodyFileSize(filePathsToUpload[0]);

            webRequestObj.uploadHandler = new UploadHandlerRaw(binaryRequest);
            webRequestObj.downloadHandler = new DownloadHandlerBuffer();
            webRequestObj.SetRequestHeader("Authorization", $"Bearer {googleDriveAccessToken}");
            webRequestObj.SetRequestHeader("Content-Type", "application/json; charset=UTF-8");
            webRequestObj.SetRequestHeader("X-Upload-Content-Type", "application/octet-stream");
            webRequestObj.SetRequestHeader("X-Upload-Content-Length", $"{fileSizeToUpload}");

            lastResponseValid = false;
            yield return webRequestObj.SendWebRequest();

            if (webRequestObj.isHttpError || webRequestObj.responseCode != 200)
            {
                //Assume not be able to upload
                Debug.LogError("[GoogleDataUploader] Unable to send Google upload request, skipping element.");
                filePathsToUpload.RemoveAt(0);    //Pop the first index;
                yield break;
            }
            string locationHeader = webRequestObj.GetResponseHeader("Location");
            if (locationHeader == null || locationHeader.Length <= 0)
            {
                Debug.LogError("[GoogleDataUploader] Couldn't get the Location response header from the File initiate web request.");
                filePathsToUpload.RemoveAt(0);    //Pop the first index;
                yield break;
            }
            lastResponseValid = true;

            int indexOfCutOffChar = locationHeader.IndexOf("&");
            locationHeader = locationHeader.Substring(locationHeader.IndexOf("&") + 1);
            currentUploadLocationId = locationHeader.Substring(locationHeader.IndexOf("&"));//So nice, you do it twice.
        }

        private static long GetRequestBodyFileSize(string fileToCheck)
        {
            FileInfo fileTocheckInfo = new FileInfo(fileToCheck);
            if (fileTocheckInfo == null)
            {
                return 0;
            }
            return fileTocheckInfo.Length;
        }

        //For uploading a file, a certain structure such as the binary file with size is required to upload the file, this function builds the binaryData.
        private static byte[] BuildBinaryFileRequestBody(string fileToCheck)
        {
            bool validFilePath = System.IO.File.Exists(fileToCheck);
            if (!validFilePath)
            {
                Debug.Log($"[GoogleDataUploader] Can't create binary upload body, `{fileToCheck}`  was not found.");
                return null;
            }

            byte[] binaryData = System.IO.File.ReadAllBytes(fileToCheck);
            if (binaryData == null)
            {
                return null;
            }

            uint binaryFileSize = (uint)binaryData.Length;
            byte[] buildUpArray = new byte[binaryFileSize];
            binaryData.CopyTo(buildUpArray, 0);    //the file itself after the second content header

            return buildUpArray;
        }

        private IEnumerator UploadResumableGoogleFile(string locationId)
        {
            //Binary request, general gooogle drive structure first, which is the name of the header according to the content type RFC 2387
            byte[] binaryRequestBody = BuildBinaryFileRequestBody(filePathsToUpload[0]);
            if (binaryRequestBody == null)
            {
                Debug.LogError("[GoogleDataUploader] Unable to load the requested file in BuildBinaryFileRequestBody, skipping element.");
                filePathsToUpload.RemoveAt(0);    //Pop the first index;
                yield break;
            }

            //Setup webrequest for upload
            webRequestObj = new UnityWebRequest($"{googleUploadFilePostUrl}&uploadType=resumable{locationId}", "PUT");

            //Set the webrequest headers and upload
            webRequestObj.uploadHandler = new UploadHandlerRaw(binaryRequestBody);
            webRequestObj.downloadHandler = new DownloadHandlerBuffer();
            webRequestObj.SetRequestHeader("Authorization", $"Bearer {googleDriveAccessToken}");
            webRequestObj.SetRequestHeader("Content-Type", "application/octet-stream");

            Debug.Log($"[GoogleDataUploader] Attempting to upload file {currentUnescapedFileName} to Google Drive ID: {locationId}");
            
            yield return webRequestObj.SendWebRequest();

            if (webRequestObj.isHttpError)
            {
                //Assume not be able to upload
                Debug.LogError("[GoogleDataUploader] Failed to upload the file correctly, skipping element this time.");
                filePathsToUpload.RemoveAt(0);    //Pop the first index;
                yield break;
            }

            Debug.Log($"[GoogleDataUploader] Successfully uploaded {currentUnescapedFileName}.");
            //Add or replace the entry in the list and save the data.
            filePathsToUpload.RemoveAt(0);    //Pop the first index;
        }
    }
}
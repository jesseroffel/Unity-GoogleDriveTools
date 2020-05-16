using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GoogleDriveTools
{
    public class GoogleDataDownloader : EditorWindow
    {
        //References in order for the tool to work or process current data
        private static string defaultDownloadTargetLocation = "";
        private static UnityWebRequest webRequestObj = null;
        private static DefaultAsset downloadTargetFolder = null;
        private static List<GoogleDriveItem> googleFolderIdHierarcyTree = new List<GoogleDriveItem>();
        private static Queue<GoogleDriveItem> googleFolderDownloadQueue = new Queue<GoogleDriveItem>();
        private static Queue<GoogleDriveItem> googleFileDownloadQueue = new Queue<GoogleDriveItem>();

        //Google Drive API related requirements
        private const string googleApiGetItemUrl = "https://www.googleapis.com/drive/v3/files";
        private const string googleAllowTeamDrive = "supportsAllDrives=true&supportsTeamDrives=true";   

        private const string defaultGoogleSharedDriveId = "[DRIVEID]";                                  // Default Shared Drive id on Google Drive used by any search
        private const string defaultGoogleDriveStartFolderId = "[FOLDERSTARTID]";                       // Default folder id on Google drive
        private const string defaultGoogleDriveStartFolderName = "";                                    // Default folder name of root
        private static string googleDriveAccessToken = "";                                              // Current received access token used to do any google call
        private static string currentFilePath = "";                                                     // Current file its path that is being processed
        private string hierarchyOfGoogleFolders = "";                                                   // String to display folder hierarchy from google
        private string googleSearchQuery = "";                                                          // Current query send out
        private static GoogleDriveSearchResults currentGoogleSearch;                                    // Used to display the current items and process recursive download
        private static GoogleDriveItem currentMetaItem;                                                 // Used to link downloaded data to a name/id

        //Progress bar variables
        private static float currentDownloadsProcessed = 0;
        private static float currentDownloadsCount = 0;

        //To indicate that the tool isn't working properly this bool is used to not display as normal if false
        private static bool validDownloadConfiguration = false;
        private static bool googleAuthenticationTokenReceived = false;
        private static bool waitingForGoogleAuthenticationToken = false;
        private static bool sendOutFirstFolderFetch = true;
        private static bool searchThroughAllItemsInDrive = true;
        private static bool downloadFoldersInCurrentFolder = true;
        private static bool forceStopCurrentDownloads = false;
        private static bool currentlyDownloading = false;
        private static bool downloadingRecursively = false;
        private static bool firstRecursiveDownload = false;

        //Gui related states
        private bool drawToolSetupWindow = true;
        private static bool canGoBackInGoogleDriveHierarchy = false;

        //Used to check what kind of file to create
        public enum GoogleFileType { NotSet, Folder, Octet };

        public struct GoogleDriveItem
        {
            public string itemId { get; private set; }
            public string itemName { get; private set; }
            public GoogleFileType itemType { get; private set; }
            public GoogleDriveItem(string googleItemId, string googleItemName, GoogleFileType googleItemType)
            {
                itemId = googleItemId;
                itemName = googleItemName;
                itemType = googleItemType;
            }
        }

        [System.Serializable]
        public class GoogleDriveSearchResults
        {
            public string kind;
            public bool incompleteSearch;
            public List<googleApiResponseItem> files;
        }

        [System.Serializable]
        public class googleApiResponseItem
        {
            public string kind;
            public string id;
            public string name;
            public string mimeType;
            public string teamDriveID;
            public string driveId;
            public GoogleFileType fileType;
        }

        //Create option to open window
        [MenuItem("Tools/Google Data Downloader")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow(typeof(GoogleDataDownloader), false, "Google Data Downloader");
        }

        private void Awake()
        {
            RequestNewGoogleDriveAccessToken();
        }

        void OnInspectorUpdate()
        {
            if (webRequestObj != null && !webRequestObj.isDone)
            {
                Repaint();
            }
            if (currentlyDownloading)
            {
                Repaint();
            }
        }

        //Drawing all UI elements and handle their states
        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Google Data Download Tool, this tool allows you as an user to download all collected Google  data quickly from Google Drive towards an assigned folder. If you are unsure how to use this tool contact Jesse or Hannes.", MessageType.Info);
            //Configuration related info
            if (drawToolSetupWindow)
            {
                DrawToolEditorSetupWindow();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Configurations", EditorStyles.boldLabel);
                if (GUILayout.Button("Show Configurations"))
                {
                    drawToolSetupWindow = true;
                }
                EditorGUILayout.EndHorizontal();
            }

            //Drive viewer related behaviour
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Google Drive Viewer", EditorStyles.boldLabel);

            DrawGoogleDriveViewer();
        }

        private void DrawToolEditorSetupWindow()
        {
            EditorGUILayout.LabelField("Configurations", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            downloadTargetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Download Target Folder", downloadTargetFolder, typeof(DefaultAsset), false);

            if (downloadTargetFolder != null)
            {
                if (GUILayout.Button("Hide Configurations"))
                {
                    drawToolSetupWindow = false;
                }

                //Set default path if folder is set
                if (defaultDownloadTargetLocation.Length == 0)
                {
                    defaultDownloadTargetLocation = AssetDatabase.GetAssetPath(downloadTargetFolder);
                }
                validDownloadConfiguration = true;
            }
            else
            {
                //Unset default path if still set
                if (defaultDownloadTargetLocation.Length != 0)
                {
                    defaultDownloadTargetLocation = "";
                }
                validDownloadConfiguration = false;
            }
            EditorGUILayout.EndHorizontal();

            //Google Drive Options
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Google Drive Id", defaultGoogleSharedDriveId);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Google Root Folder", defaultGoogleDriveStartFolderId);
            EditorGUILayout.EndHorizontal();
            //Access Token
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Refresh Google Access Token "))
            {
                RequestNewGoogleDriveAccessToken();
            }
        }

        private void DrawGoogleDriveViewer()
        {
            if (!googleAuthenticationTokenReceived && waitingForGoogleAuthenticationToken)
            {
                EditorGUILayout.HelpBox("Waiting for Google OAuth2 token", MessageType.Info);
                return;
            }

            //Valid token so display the rest.

            if (googleDriveAccessToken.Length <= 0)
            {
                return;
            }
            //Draw progress bar if any
            DrawGoogleDownloadProgress();


            //Don't bother showing the viewer when downloading is currently busy.
            if (downloadingRecursively)
            {
                return;
            }

            //Option bar
            DrawGoogleDriveOptions();

            //Display folder hierarchy.
            if (googleFolderIdHierarcyTree.Count > 0)
            {
                for (int i = 0; i < googleFolderIdHierarcyTree.Count; i++)
                {
                    if (i == 0)
                    {
                        hierarchyOfGoogleFolders = googleFolderIdHierarcyTree[i].itemName;
                        continue;
                    }
                    hierarchyOfGoogleFolders += " > " + googleFolderIdHierarcyTree[i].itemName;
                }
            }

            //Draw viewer
            DrawGoogleDriveItemField();
        }

        private void DrawGoogleDriveOptions()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!validDownloadConfiguration);   //Disable/Enable depending on correct setup to prevent errors.

            EditorGUI.BeginDisabledGroup(!canGoBackInGoogleDriveHierarchy); //Only enable this if it is possible to go up a folder.
            if (GUILayout.Button("Up Hierarchy"))
            {
                GoUpGoogleFolderHierarchy();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Refresh View"))
            {
                //Get either default or last item accessed in hierarchy
                if (googleFolderIdHierarcyTree.Count == 0)
                {
                    RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, defaultGoogleDriveStartFolderId);
                }
                else
                {
                    RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, googleFolderIdHierarcyTree[googleFolderIdHierarcyTree.Count - 1].itemId);
                }
            }
            if (GUILayout.Button("Download current folder"))
            {
                PrepareDownloadCurrentItems();
            }
            EditorGUI.BeginDisabledGroup(googleFolderIdHierarcyTree.Count != 2);   //Disable/Enable depending only on second entry to prevent downloading all.
            if (GUILayout.Button("Download all files"))
            {
                PrepareRecursiveDownload();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            
            //Allow the user to search for specific data

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();


            EditorGUI.BeginDisabledGroup(true);            // Temporary disabled to prevent the user from getting stuck when searching, will be fixed soon.

            GUILayout.Label("Name/Hash", GUILayout.Width(75));
            googleSearchQuery = GUILayout.TextField(googleSearchQuery, GUILayout.MaxWidth(200));
            searchThroughAllItemsInDrive = EditorGUILayout.Toggle("Search in current folder", searchThroughAllItemsInDrive, GUILayout.MaxWidth(175));
            
            EditorGUI.BeginDisabledGroup(googleSearchQuery.Length < 3);    //Only search for sensible queries.
            if (GUILayout.Button("Search"))
            {
                if (searchThroughAllItemsInDrive)
                {
                    RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, googleFolderIdHierarcyTree[googleFolderIdHierarcyTree.Count - 1].itemId, "", googleAllowTeamDrive);
                }
                else
                {
                    RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, googleFolderIdHierarcyTree[googleFolderIdHierarcyTree.Count - 1].itemId);
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.EndDisabledGroup();               // Temporary disabled to prevent the user from getting stuck when searching, will be fixed soon.

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGoogleDownloadProgress()
        {
            if (!currentlyDownloading)
            {
                return;
            }
            if (currentDownloadsProcessed < currentDownloadsCount)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                    "Google Downloading Progress", $"Downloading item {currentDownloadsProcessed+1} of {currentDownloadsCount}...", (currentDownloadsProcessed / currentDownloadsCount)
                ))
                {
                    Debug.Log($"[GoogleDataDownloader] Forced stop downloading files.");
                    currentlyDownloading = false;
                    forceStopCurrentDownloads = true;
                }
            }
            else
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void DrawGoogleDriveItemField()
        {
            EditorGUILayout.BeginVertical();
            
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical("GroupBox");

            if (currentGoogleSearch == null || currentGoogleSearch.files.Count == 0)
            {
                GUILayout.Label("This Google Folder is Empty");
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.LabelField(hierarchyOfGoogleFolders);
            EditorGUILayout.Space();

            //Items to show
            for (int i = 0; i < currentGoogleSearch.files.Count; i++)
            {
                GUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Label("Name", GUILayout.Width(35));
                GUILayout.TextField(currentGoogleSearch.files[i].name, GUILayout.Width(275));
                GUILayout.Label("Type", GUILayout.Width(35));
                GUILayout.TextField(currentGoogleSearch.files[i].mimeType, GUILayout.Width(85));
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(!validDownloadConfiguration);   //Disable/Enable depending on correct setup to prevent errors.
                if (currentGoogleSearch.files[i].fileType == GoogleFileType.Folder)
                {
                    if (GUILayout.Button("Open"))
                    {
                        RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, currentGoogleSearch.files[i].id, currentGoogleSearch.files[i].name);
                    }
                }
                else
                {
                    if (GUILayout.Button("Download"))
                    {
                        googleFileDownloadQueue.Enqueue(new GoogleDriveItem(currentGoogleSearch.files[i].id, currentGoogleSearch.files[i].name, currentGoogleSearch.files[i].fileType));
                        PrepareDownloadCurrentItems(true);
                    }
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        public static string CutGoogleMineType(string stringToCut)
        {
            int indexChar = stringToCut.IndexOf('/');
            if (indexChar < 0)
            {
                return string.Empty;
            }
            return stringToCut.Substring(indexChar+1);
        }

        public static void CleanupGoogleItemStruct(ref GoogleDriveSearchResults structToCheck)
        {
            if (structToCheck == null || structToCheck.files.Count <= 0)
            {
                return;
            }

            for(int i = 0; i < structToCheck.files.Count; i++)
            {
                structToCheck.files[i].mimeType = CutGoogleMineType(structToCheck.files[i].mimeType);
                if (structToCheck.files[i].mimeType.Contains("folder"))
                {
                    structToCheck.files[i].mimeType = "folder";
                    structToCheck.files[i].fileType = GoogleFileType.Folder;
                }
                else
                {
                    structToCheck.files[i].fileType = GoogleFileType.Octet;
                }
            }

        }

        public static string GetSafeFilename(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }

        private static void GoUpGoogleFolderHierarchy()
        {
            if (googleFolderIdHierarcyTree.Count <= 1)
            {
                Debug.LogError($"[GoogleDataDownloader] Cannot go back further in folder hierarchy, researched starting point.");
                return;
            }
            int currentFolderIndex = (googleFolderIdHierarcyTree.Count - 1);    //Get current item index
            GoogleDriveItem newFolder = googleFolderIdHierarcyTree[currentFolderIndex - 1]; // get previous item to go towards.
            if (string.IsNullOrEmpty(newFolder.itemId) || string.IsNullOrEmpty(newFolder.itemName))
            {
                Debug.LogError($"[GoogleDataDownloader] GoogleDriveItem invalid, cannot be used to search folders.");
            }
            //Remove current item from list and fetch previous item;
            googleFolderIdHierarcyTree.RemoveAt(currentFolderIndex);
            if (googleFolderIdHierarcyTree.Count <= 1 && canGoBackInGoogleDriveHierarchy)
            {
                canGoBackInGoogleDriveHierarchy = false;
            }
            RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, newFolder.itemId); //Leave out name to just fetch the folder but don't add to hierarchy.
        }

        private static void RequestNewGoogleDriveAccessToken()
        {
            Debug.Log("[GoogleDataDownloader] Sending Google OAuth2 request token...");
            waitingForGoogleAuthenticationToken = true;
            WWWForm apiRequestForm = new WWWForm();
            apiRequestForm.AddField("refresh_token", "[REFRESH_TOKEN]");
            apiRequestForm.AddField("client_id", "[CLIENT_ID]");
            apiRequestForm.AddField("client_secret", "[CLIENT_SECRET]");
            apiRequestForm.AddField("grant_type", "refresh_token");

            webRequestObj = new UnityWebRequest();
            webRequestObj = UnityWebRequest.Post("https://oauth2.googleapis.com/token", apiRequestForm);
            webRequestObj.SendWebRequest();

            EditorApplication.update += RetrieveGoogleAccessToken;        
        }

        private static void RetrieveGoogleAccessToken()
        {
            if (!webRequestObj.isDone)
            {
                return;
            }
            EditorApplication.update -= RetrieveGoogleAccessToken;

            waitingForGoogleAuthenticationToken = false;
            //If invalid response...
            if (webRequestObj.isHttpError || webRequestObj.responseCode != 200)
            {
                Debug.LogError($"[GoogleDataDownloader] Unable to get a new Google Drive access token. Try again or contact Tech/QA. {webRequestObj.downloadHandler.text}");
                return;
            }

            Debug.Log("[GoogleDataDownloader] Received Google OAuth2 access token!");
            googleDriveAccessToken = GoogleDataUploader.ReplaceEscapeCharacters(webRequestObj.downloadHandler.text, true);
            googleAuthenticationTokenReceived = true;
            if (sendOutFirstFolderFetch)
            {
                sendOutFirstFolderFetch = false;
                RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, defaultGoogleDriveStartFolderId, defaultGoogleDriveStartFolderName);
            }
        }

        private static void RequestGoogleDriveFolderItems(string googleDriveId, string googleFolderID, string folderName = "", string searchQuery = "", bool searchInRoot = false)
        {
            //Check if the passed item has a folder name, if not, do not included.
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                googleFolderIdHierarcyTree.Add(new GoogleDriveItem(googleFolderID, folderName, GoogleFileType.Folder));
            }

            if (firstRecursiveDownload)
            {
                firstRecursiveDownload = false;
            }

            //Check to enable the return in hierarchy button
            if (googleFolderIdHierarcyTree.Count > 1 && !canGoBackInGoogleDriveHierarchy)
            {
                canGoBackInGoogleDriveHierarchy = true;
            }

            string fetchUrl = $"{googleApiGetItemUrl}?corpora=drive&driveId={googleDriveId}&includeTeamDriveItems=true";
            
            //build specific url with right parameters to perform a get request.
            if (searchQuery != null)
            {
                if (searchInRoot)
                {
                    fetchUrl += $"&q=name%20contains%20'{searchQuery}%20and%20trashed%20!%3D%20true'&{googleAllowTeamDrive}";
                }
                else
                {
                    fetchUrl += $"&q=%27{googleFolderID}%27%20in%20parents%20and%20name%20contains%20'{searchQuery}'%20and%20trashed%20!%3D%20true&{googleAllowTeamDrive}";
                }
            }
            else
            {
                fetchUrl = $"&q=%27{googleFolderID}%27%20in%20parents%20and%20trashed%20!%3D%20true&{googleAllowTeamDrive}";
            }
/*                                "&access_token={googleDriveAccessToken}";*/
            Debug.Log($"[GoogleDataUpload] Fetching files from folder name: {folderName} - id: {googleFolderID} as parent...");

            webRequestObj = new UnityWebRequest();
            webRequestObj = UnityWebRequest.Get(fetchUrl);
            webRequestObj.SetRequestHeader("Authorization", $"Bearer {googleDriveAccessToken}");

            webRequestObj.SendWebRequest();

            EditorApplication.update += FetchGoogleDriveFolderItemsResults;
        }

        private static void FetchGoogleDriveFolderItemsResults()
        {
            if (!webRequestObj.isDone)
            {
                return;
            }
            EditorApplication.update -= FetchGoogleDriveFolderItemsResults;

            //Handle unexpected error
            if (webRequestObj.isHttpError || webRequestObj.responseCode != 200)
            {
                googleFolderIdHierarcyTree.RemoveAt(googleFolderIdHierarcyTree.Count-1);
                Debug.LogError($"[GoogleDataUpload] Unable to Fetch Google Drive folder Items.{webRequestObj.downloadHandler.text}");
                return;
            }

            //Remove certain characters from returned string ot be able to parse the string using JsonUtility 
            string escapedResultFromJson = GoogleDataUploader.ReplaceEscapeCharacters(webRequestObj.downloadHandler.text);
            GoogleDriveSearchResults returnedFolderResults = JsonUtility.FromJson<GoogleDriveSearchResults>(escapedResultFromJson);

            CleanupGoogleItemStruct(ref returnedFolderResults);   //Process returned struct for future use
            
            //Either set the currentSearch to display or add to a queue for download
            currentGoogleSearch = returnedFolderResults;
            if (downloadingRecursively)
            {
                PrepareDownloadCurrentItems();
            }
        }

        private static void PrepareDownloadCurrentItems(bool singleDownload = false)
        {
            if (currentGoogleSearch.files.Count <= 0)
            {
                Debug.Log($"[GoogleDataDownloader] No valid items to download!");
                currentlyDownloading = false;
                return;
            }

            //Check if download is already on disk.
            CreateMissingFolders(singleDownload);

            //Setup gui stuff
            currentDownloadsCount = googleFileDownloadQueue.Count;
            currentDownloadsProcessed = 0;
            if (currentDownloadsCount > 0) 
            {
                currentlyDownloading = true;
            }

            CheckCurrentDownloadQueue();
        }

        private static void CheckCurrentDownloadQueue()
        {
            if (forceStopCurrentDownloads)
            {
                forceStopCurrentDownloads = false;
                Debug.Log($"[GoogleDataDownloader] Forced stop download queue");
                return;

            }
            if (googleFileDownloadQueue.Count <= 0)
            {
                if (googleFolderDownloadQueue.Count <= 0)
                {
                    Debug.Log($"[GoogleDataDownloader] Finished download queue");
                    downloadingRecursively = false;
                    currentlyDownloading = false;
                    EditorUtility.ClearProgressBar();
                    //refresh view to where recursive started.
                    googleFolderIdHierarcyTree.RemoveAt(googleFolderIdHierarcyTree.Count-1);
                    RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, googleFolderIdHierarcyTree[googleFolderIdHierarcyTree.Count - 1].itemId);
                    return;
                }
                else
                {
                    if (!firstRecursiveDownload)
                    {
                        googleFolderIdHierarcyTree.RemoveAt(googleFolderIdHierarcyTree.Count - 1);
                    }
                    GoogleDriveItem folderToSearch = googleFolderDownloadQueue.Dequeue();
                    RequestGoogleDriveFolderItems(defaultGoogleSharedDriveId, folderToSearch.itemId, folderToSearch.itemName);
                    return;
                }
            }
            currentMetaItem = googleFileDownloadQueue.Dequeue();
            RequestDownloadGoogleDriveFile(currentMetaItem);
        }

        private static void CreateMissingFolders(bool singleDownload = false)
        {
            //Check and create missing folders based on the current folder hierarchy followed.
            string currentFolder = defaultDownloadTargetLocation;
            string nextFolder = "";
            for (int i = 0; i < googleFolderIdHierarcyTree.Count; i++)
            {
                //Skip the root for now
                if (i == 0)
                {
                    continue;
                }
                nextFolder = Path.Combine(currentFolder, GetSafeFilename(googleFolderIdHierarcyTree[i].itemName));
                currentFolder = nextFolder;
                if (Directory.Exists(nextFolder))
                {
                    continue;
                }
                Directory.CreateDirectory(nextFolder);
            }
            currentFilePath = currentFolder;

            //Create folders in current if required and add files to download.
            for (int i = 0; i < currentGoogleSearch.files.Count; i++)
            {
                if (currentGoogleSearch.files[i].fileType == GoogleFileType.Folder && downloadFoldersInCurrentFolder)
                {
                    string newFolder = Path.Combine(currentFolder, GetSafeFilename(currentGoogleSearch.files[i].name));
                    if (Directory.Exists(newFolder))
                    {
                        continue;
                    }
                    Directory.CreateDirectory(newFolder);
                    continue;
                }
                if (!singleDownload)
                {
                    googleFileDownloadQueue.Enqueue(new GoogleDriveItem(currentGoogleSearch.files[i].id, currentGoogleSearch.files[i].name, GoogleFileType.Octet));
                }
            }
        }

        private static void RequestDownloadGoogleDriveFile(GoogleDriveItem itemToDownload)
        {
            string fetchUrl = $@"{googleApiGetItemUrl}/{itemToDownload.itemId}?{googleAllowTeamDrive}&alt=media";

            Debug.Log($"[GoogleDataUpload] Attempting to download: {itemToDownload.itemName}");

            webRequestObj = new UnityWebRequest();
            webRequestObj = UnityWebRequest.Get(fetchUrl);
            webRequestObj.SetRequestHeader("Authorization", $"Bearer {googleDriveAccessToken}");

            webRequestObj.SendWebRequest();

            EditorApplication.update += RetrieveCurrentFileDownloadRequest;
        }
        private static void RetrieveCurrentFileDownloadRequest()
        {
            if (!webRequestObj.isDone)
            {
                return;
            }
            EditorApplication.update -= RetrieveCurrentFileDownloadRequest;


            //If invalid response...
            if (webRequestObj.isHttpError || webRequestObj.responseCode != 200)
            {
                Debug.LogError($"[GoogleDataDownloader] Unable to retrieve the current file, skipping to next.{webRequestObj.downloadHandler.text}");
                return;
            }
            
            ProcessReceivedBinaryData(webRequestObj.downloadHandler.data, currentMetaItem, currentFilePath);
            currentDownloadsProcessed++;
            CheckCurrentDownloadQueue();
        }

        private static void ProcessReceivedBinaryData(byte[] dataToWrite, GoogleDriveItem metaDataToReference, string pathToExport)
        {
            //Over here I can add a switch later if I want to support more types.
            if (metaDataToReference.itemType == GoogleFileType.NotSet)
            {
                Debug.LogError($"[GoogleDataDownloader] Unable to process received data due to passed meta file not having a type set.");
                return;
            }

            if (metaDataToReference.itemType == GoogleFileType.Octet)
            {
                FileStream fs = new FileStream(Path.Combine(pathToExport, GetSafeFilename(metaDataToReference.itemName)), FileMode.OpenOrCreate, FileAccess.Write);
                if (fs == null)
                {
                    Debug.LogError($"[GoogleDataDownloader] Unable create/open file {GetSafeFilename(metaDataToReference.itemName)}.");
                    return;
                }
                fs.Write(dataToWrite, 0, dataToWrite.Length);
                fs.Close();
                Debug.Log($"[GoogleDataUpload] Created new octet-stream file {metaDataToReference.itemName} at {pathToExport}");
            }
        }

        private static void PrepareRecursiveDownload()
        {
            if (currentGoogleSearch.files.Count <= 0)
            {
                Debug.LogError($"[GoogleDataUpload] Nothing to download");
                return;
            }

            if (googleFolderDownloadQueue.Count > 0)
            {
                googleFolderDownloadQueue.Clear();
            }

            for(int i = 0; i < currentGoogleSearch.files.Count; i++)
            {
                if (currentGoogleSearch.files[i].fileType == GoogleFileType.Folder)
                {
                    googleFolderDownloadQueue.Enqueue(new GoogleDriveItem(currentGoogleSearch.files[i].id, currentGoogleSearch.files[i].name, currentGoogleSearch.files[i].fileType));
                }
                else
                {
                    googleFileDownloadQueue.Enqueue(new GoogleDriveItem(currentGoogleSearch.files[i].id, currentGoogleSearch.files[i].name, currentGoogleSearch.files[i].fileType));
                }
            }
            downloadingRecursively = true;
            firstRecursiveDownload = true;
            PrepareDownloadCurrentItems();
        }
    }
}


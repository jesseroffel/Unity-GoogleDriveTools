# Unity Google Drive Tools

For the project [Spellbound Spire](https://jesseroffel.com/projects/spellbound-spire.html) we used Google Drive to store certain analytic data about a player. As this data was processed in a different tool and we needed per level all available analytic data I created these small scripts which can `Upload` files and an editor tool that can `Download` files.

The goal is to easily Upload any file and Download one or multiple files to or from the Google Drive quickly while developing using the Unity Engine.


**NOTE:** A demo video and demo scene will soon be set up to quickly see the tools in action.

)



## Set it up yourself

Both inside the `GoogleDataUploader.cs` and `GoogleDataDownloader.cs` files there are missing fields that need to be filled in before the tool can work.

**Google Drive Setup**

* [DRIVEID] => (Shared)Drive Id as found in the url
* [DRIVEROOTID] / [FOLDERSTARTID] => Optional starter Id of a folder within the Drive.



Both scripts make use of the [Google Drive V3 API](https://developers.google.com/drive/api/v3/reference), which requires [OAuth2](https://developers.google.com/identity/protocols/oauth2?hl=en_GB) access tokens to be used for each web request. 

Setup if required the access to the API services here: https://console.developers.google.com.

**Google OAuth2 Access Token Setup**

* [REFRESH_TOKEN] => Google OAuth2 Refresh Token
* [CLIENT_ID] =>  Google OAuth2 Client ID
* [CLIENT_SECRET] =>   Google OAuth2 Client Secret



## Uploading

Uploading is done with a singleton class `GoogleDataUploader.cs` which has to exist within an active scene to be access.

Using a single static call `UploadLocalFileToGoogleDrive()` it can upload a file asynchronously to the matching Google Drive Folder.

Currently the accepted files are binary, options can be set to create a separate folder based on the filename which we used to categorise per level, any upload can have a GUID attached to the file name to anonymously upload a file with an identification in place.

[Link to demo gif](https://drive.google.com/file/d/1JzpBf6iAIcMDMNoQKpvrsCQ6JeTIFuBX)

## Downloading Editor Tool

Downloading can be done through an Unity engine editor tool class `GoogleDataDownloader.cs`, this tool will provide an basic Google Drive file directory viewer with download options and configuration settings.

A simple configuration screen and navigation allows for quick downloading of one or multiple files without much hassle.

Any file can be currently downloaded towards the target folder, downloading a current folder will download files recursively if set, including folders matching the Google Drive folder online.

[Link to preview vid](https://drive.google.com/open?id=1JAI3Vjw3niXKyJT1j7mW70Ab1OPBZ5pa) 



### More to come

This project will get updated as I add more functionality to these tools if the projects requires so. I'll share the source with including material whenever possible. For now, check out the project on my portfolio website; [Spellbound Spire](https://jesseroffel.com/projects/spellbound-spire.html).
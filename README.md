# AzureBackup
This is a console application for backing up local files to Azure blob storage. It will scan through all the files in the provided folder paths and upload all those that don't already exist and overwrite those that do exist but that have been modified more recently on the local machine than the version in blob storage. This application won't overwite local files with the remote version if it has been modified more recently, nor will it delete local files if the remote version has been removed.

# Setup
Create an appsettings.json file in the project root with the following structure
```
{
  "Azure": {
    "ContainerConnectionString": "<YOUR_CONNECTION_STRING>",
    "ContainerName": "<YOUR_CONTAINER_NAME>",
    "Logging": "",
    "BlobUpload": {
      "ParallelOperationThreadCount": "1",
      "SingleBlobUploadThresholdInBytes": "1048576"
    }
  },

  "Backup": {
    "Sources": [
      "C:\\Documents",
      "C:\\Music",
      "C:\\Pictures"
    ],
    "BoundedCapacity": 1000,
    "MaxDegreeOfParallelism": 1
  } 
}
```

The connection string is a shared access signature which can be generated from within [Azure Storage Explorer](https://azure.microsoft.com/en-gb/features/storage-explorer/), it should be granted read, write, delete and list rights.

Sources is an array of folder paths that contain files to be backedup.

# Usage
I usually run this app straight from the command line but it would probably make more sense to create a scheduled task in windows to run it daily if something more regular is required.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureBackup.Services
{
    public interface IBackupService
    {
        Task Run(string source);
    }

    class BackupService : IBackupService
    {
        private readonly ILogger<BackupService> _logger;
        private readonly IConfigurationRoot _config;

        public BackupService(ILogger<BackupService> logger, IConfigurationRoot config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task BackupFile(DirectoryInfo directory, FileInfo file)
        {
            _logger.LogDebug("Starting to process file {@SourceFilePath}", file.FullName);
            // Retrieve a reference to a container.
            CloudBlobContainer container = new CloudBlobContainer(new Uri(_config["Azure:ContainerConnectionString"]));

            // Upload files
            string blobPath = file.FullName.Replace(directory.FullName, "");
            blobPath = directory.Name + "\\" + blobPath;

            // Retrieve reference to a blob
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobPath);

            BlobRequestOptions blockBlobOptions = new BlobRequestOptions();
            blockBlobOptions.ParallelOperationThreadCount = int.Parse(_config["Azure:BlobUpload:ParallelOperationThreadCount"].ToString());
            blockBlobOptions.SingleBlobUploadThresholdInBytes = long.Parse(_config["Azure:BlobUpload:SingleBlobUploadThresholdInBytes"].ToString());

            _logger.LogDebug("Checking if file already exists in storage account");
            // Only upload if updated or doesn't already exist
            if (await blockBlob.ExistsAsync())
            {
                _logger.LogDebug("File already exists");
                // Only overwrite if local copy has been updated more recently
                if (file.LastWriteTimeUtc > blockBlob.Properties.LastModified)
                {
                    _logger.LogDebug("Uploading file");
                    // Overwrite the blob with contents from a local file.
                    using (FileStream fileStream = File.OpenRead(file.FullName))
                    {
                        await blockBlob.UploadFromStreamAsync(fileStream, null, blockBlobOptions, new OperationContext());
                    }
                    _logger.LogDebug("Finished file upload");
                }
                else
                {
                    _logger.LogDebug("File hasn't been modified since upload, skipping");
                }
            }
            // If doesn't exist then upload
            else
            {
                _logger.LogDebug("File doesn't already exist");
                _logger.LogDebug("Uploading file");
                // Create the blob with contents from a local file.
                using (FileStream fileStream = File.OpenRead(file.FullName))
                {
                    await blockBlob.UploadFromStreamAsync(fileStream, null, blockBlobOptions, new OperationContext());
                }
                _logger.LogDebug("Finished file upload");
            }
        }

        public async Task Run(string source)
        {
            _logger.LogDebug("Checking source directory {@SourceDirectory} exists", source);
            if (!source.EndsWith("\\"))
            {
                source += "\\";
            }

            // Check directory exists
            if (!Directory.Exists(source))
            {
                throw new ArgumentException("Specified directory doesn't exist.");
            }

            DirectoryInfo directory = new DirectoryInfo(source);

            _logger.LogDebug("Listing all files in source");
            string[] filePaths = Directory.GetFiles(source, "*", SearchOption.AllDirectories);

            List<FileInfo> files = filePaths.Select(x => new FileInfo(x)).ToList();

            foreach (FileInfo file in files)
            {
                try
                {
                    await BackupFile(directory, file);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Upload of file failed.");
                }
            }
        }
    }
}

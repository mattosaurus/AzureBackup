using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AzureBackup.Services
{
    using AzureBackup.Extensions;

    public interface IBackupService
    {
        Task Run(string source);
    }

    internal class BackupService : IBackupService
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
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            _logger.LogDebug("Starting to process file {@SourceFilePath}", file.FullName);
            // Retrieve a reference to a container.
            CloudBlobContainer container = new CloudBlobContainer(new Uri(_config["Azure:ContainerConnectionString"]));

            // Upload files
            string blobPath = file.FullName.Replace(directory.FullName, "");
            blobPath = Path.Combine(directory.Name, blobPath);

            // Retrieve reference to a blob
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobPath);

            BlobRequestOptions blockBlobOptions = new BlobRequestOptions
            {
                ParallelOperationThreadCount = int.Parse(_config["Azure:BlobUpload:ParallelOperationThreadCount"].ToString()),
                SingleBlobUploadThresholdInBytes = long.Parse(_config["Azure:BlobUpload:SingleBlobUploadThresholdInBytes"].ToString())
            };

            _logger.LogDebug("Checking if file already exists in storage account");
            // Only upload if updated or doesn't already exist
            if (await blockBlob.ExistsAsync())
            {
                _logger.LogDebug("File already exists");
                // Only overwrite if local copy has been updated more recently
                if (file.LastWriteTimeUtc > blockBlob.Properties.LastModified
                    && !blockBlob.ValidateMD5(file.FullName))
                {
                    _logger.LogDebug("Uploading file");
                    // Overwrite the blob with contents from a local file.
                    using (FileStream fileStream = File.OpenRead(file.FullName))
                    {
                        await blockBlob.UploadFromStreamAsync(fileStream, null, blockBlobOptions, new OperationContext());
                    }
                    _logger.LogDebug("Finished file upload");

                    if (!blockBlob.ValidateMD5(file.FullName))
                    {
                        _logger.LogCritical("Hashes are not equal! {@path}", file.FullName);
                    }
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

                if (!blockBlob.ValidateMD5(file.FullName))
                {
                    _logger.LogCritical("Hashes are not equal! {@path}", file.FullName);
                }
            }
        }

        public async Task Run(string source)
        {
            _logger.LogDebug("Checking source directory {@SourceDirectory} exists", source);
            if (!source.EndsWith(Path.DirectorySeparatorChar))
            {
                source += Path.DirectorySeparatorChar;
            }

            // Check directory exists
            if (!Directory.Exists(source))
            {
                throw new ArgumentException("Specified directory doesn't exist.");
            }

            _logger.LogDebug("Listing all files in source");
            var fileNames = from dir in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                            select dir;

            var dirInfo = new DirectoryInfo(source);

            foreach (var fileName in fileNames)
            {
                try
                {
                    await BackupFile(dirInfo, new FileInfo(fileName));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Upload of file failed.");
                }
            }
        }
    }
}
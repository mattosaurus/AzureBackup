using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace AzureBackup.Services
{
    public interface IBackupService
    {
        Task Run(string source, bool restore = false);
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
                if (file.LastWriteTimeUtc > blockBlob.Properties.LastModified)
                {
                    _logger.LogDebug("Uploading file");
                    // Overwrite the blob with contents from a local file.
                    using (FileStream fileStream = File.OpenRead(file.FullName))
                    {
                        await blockBlob.UploadFromStreamAsync(fileStream, null, blockBlobOptions, new OperationContext());
                    }
                    _logger.LogDebug("Finished file upload");

                    if (!CheckHashes(blockBlob, file.FullName))
                    {
                        _logger.LogCritical("Hashes not unique! {@path}", file.FullName);
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

                if (!CheckHashes(blockBlob, file.FullName))
                {
                    _logger.LogCritical("Hashes not unique! {@path}", file.FullName);
                }
            }
        }

        private async Task RestoreFilesFromContainer(CloudBlobContainer container, string target)
        {
            BlobContinuationToken continuationToken = null;
            do
            {
                var results = await container.ListBlobsSegmentedAsync(null, continuationToken);

                // Get the value of the continuation token returned by the listing call.
                continuationToken = results.ContinuationToken;

                foreach (var result in results.Results)
                {
                    _logger.LogDebug("item url {@itemUri}", result.Uri);

                    if (result is CloudBlobDirectory dir)
                    {
                        await RestoreFromDirectory(dir, target);
                    }
                    else if (result is CloudBlockBlob)
                    {
                        await RestoreFile(result, target);
                    }
                }
            }
            while (continuationToken != null); // Loop while the continuation token is not null.
        }

        private async Task RestoreFromDirectory(CloudBlobDirectory dir, string target)
        {
            if (dir == null)
            {
                throw new ArgumentNullException(nameof(dir));
            }

            BlobContinuationToken continuationToken = null;
            do
            {
                // Get the value of the continuation token returned by the listing call.
                var results = await dir.ListBlobsSegmentedAsync(continuationToken);

                foreach (var result in results.Results)
                {
                    if (result is CloudBlobDirectory)
                    {
                        await RestoreFromDirectory(result as CloudBlobDirectory, target);
                    }
                    else if (result is CloudBlockBlob)
                    {
                        await RestoreFile(result, target);
                    }
                }
            }
            while (continuationToken != null); // Loop while the continuation token is not null.
        }

        private static string CreateHash(byte[] bytes)
        {
            using (var md5 = MD5.Create())
            {
                var md5Hash = md5.ComputeHash(bytes);
                return Convert.ToBase64String(md5Hash);
            }
        }

        private static bool CheckHashes(CloudBlockBlob blob, string path)
        {
            var contentMD5 = blob.Properties.ContentMD5;
            var fileContentMD5 = CreateHash(File.ReadAllBytes(path));
            return contentMD5 == fileContentMD5;
        }

        private async Task RestoreFile(IListBlobItem item, string target)
        {
            if (item == null || !(item is CloudBlockBlob blockBlob))
            {
                throw new ArgumentNullException(nameof(item));
            }

            _logger.LogDebug(blockBlob.Name);
            _logger.LogDebug(blockBlob.Properties.LastModified.ToString());

            var targetDirName = new DirectoryInfo(target).Name;
            var fileName = blockBlob.Name.Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(target, fileName)
                .Replace(Path.Combine(targetDirName, targetDirName), targetDirName);

            if (!File.Exists(path))
            {
                await blockBlob.DownloadToFileAsync(path, FileMode.CreateNew);
                _logger.LogDebug(@"item created");
            }
            else if (File.GetLastWriteTimeUtc(path) < blockBlob.Properties.LastModified)
            {
                if (!CheckHashes(blockBlob, path))
                {
                    await blockBlob.DownloadToFileAsync(path, FileMode.Create);
                    _logger.LogDebug(@"item overwritten");
                }
            }
            if (!CheckHashes(blockBlob, path))
            {
                _logger.LogCritical("Hashes not unique! {@path}", path);
            }

            _logger.LogDebug("");
            _logger.LogDebug(@"==========================");
        }

        public async Task Run(string source, bool restore = false)
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

            // backup
            if (restore == false)
            {
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

                return;
            }

            // restore
            _logger.LogDebug("Starting to process file restore to {@SourceFilePath}", source);

            // Retrieve a reference to a container.
            CloudBlobContainer container = new CloudBlobContainer(new Uri(_config["Azure:ContainerConnectionString"]));

            try
            {
                await RestoreFilesFromContainer(container, source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download from container failed.");
            }
        }
    }
}
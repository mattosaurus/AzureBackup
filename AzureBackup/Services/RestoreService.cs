using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AzureBackup.Services
{
    using AzureBackup.Extensions;

    public interface IRestoreService
    {
        Task Run(string target);
    }

    internal class RestoreService : IRestoreService
    {
        private readonly ILogger<BackupService> _logger;
        private readonly IConfigurationRoot _config;

        public RestoreService(ILogger<BackupService> logger, IConfigurationRoot config)
        {
            _logger = logger;
            _config = config;
        }

        private async Task IterateBlobsFromContainer(CloudBlobContainer container, string target)
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
                        await IterateBlobsFromDirectory(dir, target);
                    }
                    else if (result is CloudBlockBlob blockBlob)
                    {
                        await RestoreFile(blockBlob, target);
                    }
                }
            }
            while (continuationToken != null); // Loop while the continuation token is not null.
        }

        private async Task IterateBlobsFromDirectory(CloudBlobDirectory dir, string target)
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
                        await IterateBlobsFromDirectory(result as CloudBlobDirectory, target);
                    }
                    else if (result is CloudBlockBlob blockBlob)
                    {
                        await RestoreFile(blockBlob, target);
                    }
                }
            }
            while (continuationToken != null); // Loop while the continuation token is not null.
        }

        public async Task RestoreFile(CloudBlockBlob blockBlob, string target)
        {
            if (blockBlob == null)
            {
                throw new ArgumentNullException(nameof(blockBlob));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
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
                if (!blockBlob.ValidateMD5(path))
                {
                    await blockBlob.DownloadToFileAsync(path, FileMode.Create);
                    _logger.LogDebug(@"item overwritten");
                }
            }
            if (!blockBlob.ValidateMD5(path))
            {
                _logger.LogCritical("Hashes are not equal! {@path}", path);
            }

            _logger.LogDebug("");
            _logger.LogDebug(@"==========================");
        }

        public async Task Run(string target)
        {
            _logger.LogDebug("Checking source directory {@SourceDirectory} exists", target);
            if (!target.EndsWith(Path.DirectorySeparatorChar))
            {
                target += Path.DirectorySeparatorChar;
            }

            // Check directory exists
            if (!Directory.Exists(target))
            {
                throw new ArgumentException("Specified directory doesn't exist.");
            }

            // restore
            _logger.LogDebug("Starting to process file restore to {@SourceFilePath}", target);

            // Retrieve a reference to a container.
            CloudBlobContainer container = new CloudBlobContainer(new Uri(_config["Azure:ContainerConnectionString"]));

            try
            {
                await IterateBlobsFromContainer(container, target);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download from container failed.");
            }
        }
    }
}
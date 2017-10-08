using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AzureBackup
{
    class Program
    {
        static void Main(string[] args)
        {
            // Configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            IConfigurationRoot configuration = builder.Build();

            string backupPath = args[0];

            if (!backupPath.EndsWith("\\"))
            {
                backupPath += "\\";
            }

            // Check directory exists
            if (!Directory.Exists(backupPath))
            {
                throw new ArgumentException("Specified directory doesn't exist.");
            }

            DirectoryInfo directoryInfo = new DirectoryInfo(backupPath);

            string[] filePaths = Directory.GetFiles(backupPath, "*", SearchOption.AllDirectories);

            List<FileInfo> files = filePaths.Select(x => new FileInfo(x)).ToList();

            // Retrieve storage account from connection string.
            string containerConnectionString = configuration["Azure:ContainerConnectionString"];
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(containerConnectionString);

            // Create the blob client.
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve a reference to a container.
            CloudBlobContainer container = blobClient.GetContainerReference(configuration["Azure:ContainerName"]);

            // Upload files
            foreach (FileInfo file in files)
            {
                string blobPath = file.FullName.Replace(backupPath, "");
                blobPath = directoryInfo.Name + "\\" + blobPath;

                // Retrieve reference to a blob
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(file.Name);

                // Create or overwrite the "myblob" blob with contents from a local file.
                using (FileStream fileStream = File.OpenRead(file.FullName))
                {
                    blockBlob.UploadFromStreamAsync(fileStream);
                }
            }
        }
    }
}

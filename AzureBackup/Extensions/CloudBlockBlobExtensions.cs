using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Security.Cryptography;

namespace AzureBackup.Extensions
{
    public static class CloudBlockBlobExtensions
    {
        public static bool HashesAreEqual(this CloudBlockBlob blob, string filePath)
        {
            return blob.Properties.ContentMD5.Equals(CalculateMD5(filePath));
        }

        private static string CalculateMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return Convert.ToBase64String(hash);
                }
            }
        }
    }
}
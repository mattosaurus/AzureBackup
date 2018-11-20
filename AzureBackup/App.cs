using AzureBackup.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using System;
using System.Threading.Tasks;

namespace AzureBackup
{
    public class App
    {
        private readonly IBackupService _backupService;
        private readonly IRestoreService _restoreService;
        private readonly ILogger<App> _logger;
        private readonly IConfigurationRoot _config;

        public App(IBackupService backupService, IRestoreService restoreService, IConfigurationRoot config, ILogger<App> logger)
        {
            _backupService = backupService;
            _restoreService = restoreService;
            _logger = logger;
            _config = config;
        }

        public async Task Run(string source, bool restore = false)
        {
            string logKey = Guid.NewGuid().ToString();

            // Push ID to log
            using (LogContext.PushProperty("LogKey", logKey))
            {
                if (restore)
                {
                    await _restoreService.Run(source);
                }
                else
                {
                    await _backupService.Run(source);
                }
            }

            _logger.LogInformation("Ending Service for {@BackupFile} with LogKey {@ID}", source, logKey);
        }
    }
}
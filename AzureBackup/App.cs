using AzureBackup.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AzureBackup
{
    public class App
    {
        private readonly IBackupService _backupService;
        private readonly ILogger<App> _logger;
        private readonly IConfigurationRoot _config;

        public App(IBackupService backupService, IConfigurationRoot config, ILogger<App> logger)
        {
            _backupService = backupService;
            _logger = logger;
            _config = config;
        }

        public async Task Run(string source)
        {
            string logKey = Guid.NewGuid().ToString();

            // Push ID to log
            using (LogContext.PushProperty("LogKey", logKey))
            {
                await _backupService.Run(source);
            }

            _logger.LogInformation("Ending Service for {@BackupFile} with LogKey {@ID}", source, logKey);
        }
    }
}

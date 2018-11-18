using AzureBackup.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace AzureBackup
{
    internal class Program
    {
        public static IConfigurationRoot configuration;

        private static void Main(string[] args)
        {
            // Start!
            MainAsync((args.Length != 0) ? args[0].Contains("restore") : false).Wait();
        }

        private static async Task MainAsync(bool restore = false)
        {
            // Create service collection
            ServiceCollection serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            // Create service provider
            IServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            // Get backup sources for client
            List<string> sources = configuration.GetSection("Backup:Sources").GetChildren().Select(x => x.Value).ToList();

            // Run all tasks
            //await Task.WhenAll(sources.Select(i => serviceProvider.GetService<App>().Run(i)).ToArray());

            // Create a block with an asynchronous action
            var block = new ActionBlock<string>(
                async x => await serviceProvider.GetService<App>().Run(x, restore),
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = int.Parse(configuration["Backup:BoundedCapacity"]), // Cap the item count
                    MaxDegreeOfParallelism = int.Parse(configuration["Backup:MaxDegreeOfParallelism"])
                    //MaxDegreeOfParallelism = Environment.ProcessorCount, // Parallelize on all cores
                });

            // Add items to the block and asynchronously wait if BoundedCapacity is reached
            foreach (string source in sources)
            {
                await block.SendAsync(source);
            }

            block.Complete();
            await block.Completion;
        }

        private static void ConfigureServices(IServiceCollection serviceCollection)
        {
            // Add logging
            serviceCollection.AddSingleton(new LoggerFactory()
                .AddConsole()
                .AddSerilog()
                .AddDebug());
            serviceCollection.AddLogging();

            // Build configuration
            configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                .AddJsonFile("appsettings.json", false)
                .Build();

            //EmailConnectionInfo emailConnection = new EmailConnectionInfo();
            //emailConnection.FromEmail = configuration["Email:FromEmail"].ToString();
            //emailConnection.ToEmail = configuration["Email:ToEmail"].ToString();
            //emailConnection.MailServer = configuration["Email:MailServer"].ToString();
            //emailConnection.NetworkCredentials = new NetworkCredential(configuration["Email:UserName"].ToString(), configuration["Email:Password"].ToString());
            //emailConnection.Port = int.Parse(configuration["Email:Port"]);

            // Initialize serilog logger
            Log.Logger = new LoggerConfiguration()
                 //.WriteTo.MSSqlServer(configuration.GetConnectionString("LoggingSQLServer"), "logs")
                 //.WriteTo.Email(emailConnection, restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error, mailSubject: "Azure Backup Error")
                 .WriteTo.Console(Serilog.Events.LogEventLevel.Debug)
                 .MinimumLevel.Debug()
                 .Enrich.FromLogContext()
                 .CreateLogger();

            // Add access to generic IConfigurationRoot
            serviceCollection.AddSingleton<IConfigurationRoot>(configuration);

            // Add services
            serviceCollection.AddTransient<IBackupService, BackupService>();

            // Add app
            serviceCollection.AddTransient<App>();
        }
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Velopack;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager
{
    class Program
    {
        [STAThread]
        public static async Task Main(string[] args)
        {
            AppDataPaths.MigrateKnownFiles();
            var logPath = AppDataPaths.GetPath("startup.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now}] Application starting\n");
            }
            catch { }

            try
            {
                // Handle Velopack update operations first
                if (args.Length > 0 && args[0].StartsWith("--velo"))
                {
                    VelopackApp.Build().Run();
                    return;
                }

                VelopackApp.Build().Run();

                ConfigService.Load();
                Services.LocalizationService.Load();
                await AuthService.InitializeAsync();

                // Initialize environment and new services
                try
                {
                    EnvironmentConfigService.Initialize();
                    ErrorReportingService.Initialize();
                    PerformanceMetricsService.Initialize();
                    BackupHistoryService.Initialize();
                    BackupSchedulingService.Initialize();
                    
                    // Initialize additional services that have Initialize methods
                    BackupRetentionService.Initialize();
                    BackupRetryService.Initialize();
                    
                    // Services requiring database URL and username
                    var currentUser = AuthService.CurrentUser;
                    var dbUrl = "https://pinaypal-backup-manager-default-rtdb.firebaseio.com/";
                    var username = currentUser?.Username ?? "system";
                    
                    RealtimeMonitoringService.Initialize(dbUrl, username);
                    SystemStatusService.Initialize(dbUrl, username);
                    FirebaseRemoteService.Initialize(dbUrl, username);
                    
                    // Services requiring specific parameters
                    FileDownloadService.Initialize(username, AppDataPaths.CurrentDirectory);
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now}] Service initialization error: {ex}\n");
                }

                var services = new ServiceCollection();
                services.AddSingleton<Services.BackupManager>();
                var provider = services.BuildServiceProvider();
                Services.ServiceLocator.Provider = provider;

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                var logDir = Path.GetDirectoryName(logPath)!;
                Directory.CreateDirectory(logDir);
                File.AppendAllText(logPath, $"[{DateTime.Now}] FATAL ERROR: {ex}\n{ex.StackTrace}\n");
                throw;
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}

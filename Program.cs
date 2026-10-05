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
        public static void Main(string[] args)
        {
            // Velopack lifecycle/install hooks must run first before mutexes, single instance checks, or UI
            VelopackApp.Build().Run();

            AppIconHelper.EnsureAppUserModelId();
            if (!AppIconHelper.CheckSingleInstanceAndSignalExisting())
            {
                return;
            }

            AppDataPaths.MigrateKnownFiles();
            var logPath = AppDataPaths.GetLogPath("startup.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now}] Application starting\n");
            }
            catch { }

            try
            {
                ConfigService.Load();
                NotificationService.LoadSettings();
                TelegramService.Initialize();
                Services.LocalizationService.Load();
                AuthService.InitializeAsync().GetAwaiter().GetResult();

                // Initialize environment and new services
                try
                {
                    EnvironmentConfigService.Initialize();
                    ErrorReportingService.Initialize();
                    PerformanceMetricsService.Initialize();
                    BackupHistoryService.Initialize();
                    BackupSchedulingService.Initialize();

                    // Smart scheduling: bandwidth sampling gates deferred backups,
                    // and the fleet heartbeat watches your other PCs for unexpected outages.
                    BackupPolicyService.Start();
                    ComputerManagementService.StartHeartbeatMonitor();
                    
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

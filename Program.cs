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
        /// <summary>
        /// Resolves the startup log path without throwing. AppDataPaths touches the
        /// filesystem, which is exactly what can be broken right after an update, so
        /// this must never be allowed to fail the boot.
        /// </summary>
        private static string SafeStartupLogPath()
        {
            try
            {
                return AppDataPaths.GetLogPath("startup.log");
            }
            catch
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "pinaypal-startup.log");
            }
        }

        /// <summary>Best-effort append that never throws, so logging can't become the crash.</summary>
        private static void AppendStartupLog(string path, string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, $"[{DateTime.Now}] {message}\n");
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // Velopack lifecycle/install hooks must run first before mutexes, single instance checks, or UI
            //
            // These used to run completely unguarded, above every try/catch, so any
            // exception here terminated the process with nothing written to startup.log.
            // That is exactly the cold-launch-and-after-update crash signature:
            // VelopackApp.Run() is what services the install/update hooks and re-launch
            // after an update, and MigrateKnownFiles() runs on every start.
            var logPathEarly = SafeStartupLogPath();

            try
            {
                VelopackApp.Build().Run();
            }
            catch (Exception ex)
            {
                AppendStartupLog(logPathEarly, $"Velopack lifecycle hooks failed (continuing): {ex}");
            }

            try
            {
                AppIconHelper.EnsureAppUserModelId();
            }
            catch (Exception ex)
            {
                // Cosmetic only - a missing taskbar icon must never stop the app booting.
                AppendStartupLog(logPathEarly, $"AppUserModelId setup failed (continuing): {ex}");
            }

            try
            {
                if (!AppIconHelper.CheckSingleInstanceAndSignalExisting())
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendStartupLog(logPathEarly, $"Single-instance check failed (continuing): {ex}");
            }

            try
            {
                AppDataPaths.MigrateKnownFiles();
            }
            catch (Exception ex)
            {
                AppendStartupLog(logPathEarly, $"Data migration failed (continuing): {ex}");
            }

            var logPath = logPathEarly;
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
                    // Was an unguarded File.AppendAllText, so a failed write here threw
                    // straight into the outer handler, which then tried to log again.
                    AppendStartupLog(logPath, $"Service initialization error: {ex}");
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

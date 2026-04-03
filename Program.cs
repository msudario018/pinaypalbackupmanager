using System;
using System.IO;
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
                AuthService.Initialize();

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

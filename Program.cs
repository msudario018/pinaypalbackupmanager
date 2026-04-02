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
            try
            {
                // Log startup
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager", "startup.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now}] Application starting...\n");

                VelopackApp.Build().Run();

                ConfigService.Load();
                AuthService.Initialize();

                // Setup DI
                var services = new ServiceCollection();
                services.AddSingleton<Services.BackupManager>();
                services.AddSingleton<UI.ViewModels.FtpViewModel>(sp => new UI.ViewModels.FtpViewModel(sp.GetRequiredService<Services.BackupManager>()));

                var provider = services.BuildServiceProvider();
                Services.ServiceLocator.Provider = provider;

                File.AppendAllText(logPath, $"[{DateTime.Now}] Starting Avalonia app...\n");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // Log error and show message
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager", "startup.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now}] FATAL ERROR: {ex}\n");
                
                System.Windows.Forms.MessageBox.Show($"Application failed to start: {ex.Message}\n\nLog file: {logPath}", "Startup Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}

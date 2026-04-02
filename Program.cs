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
                // Initialize logging FIRST
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager", "startup.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now}] Application starting with args: {string.Join(", ", args)}\n");

                // Handle Velopack updates
                File.AppendAllText(logPath, $"[{DateTime.Now}] Initializing Velopack...\n");
                var vp = VelopackApp.Build();
                vp.Run();
                File.AppendAllText(logPath, $"[{DateTime.Now}] Velopack initialized successfully\n");

                // Check if this is just an update operation
                if (args.Length > 0 && args[0] == "--velo")
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now}] Velopack update operation detected, exiting\n");
                    return;
                }

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
                File.AppendAllText(logPath, $"[{DateTime.Now}] FATAL ERROR: {ex}\n{ex.StackTrace}\n");
                
                try
                {
                    System.Windows.Forms.MessageBox.Show($"Application failed to start: {ex.Message}\n\nLog file: {logPath}", "Startup Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
                catch
                {
                    // If even MessageBox fails, just write to log
                    File.AppendAllText(logPath, $"[{DateTime.Now}] Failed to show error dialog\n");
                }
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}

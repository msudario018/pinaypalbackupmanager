using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Velopack;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();
        [STAThread]
        public static void Main(string[] args)
        {
            // Allocate console for debugging
            AllocConsole();
            Console.WriteLine($"[{DateTime.Now}] Application starting with args: {string.Join(", ", args)}");
            
            // Initialize logging BEFORE ANYTHING ELSE
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PinayPalBackupManager", "startup.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now}] Application starting with args: {string.Join(", ", args)}\n");
                Console.WriteLine($"[{DateTime.Now}] Log initialized at: {logPath}");
            }
            catch
            {
                Console.WriteLine($"[{DateTime.Now}] Failed to initialize logging");
                // If logging fails, we can't do much
            }

            try
            {
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
                // Log error
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now}] FATAL ERROR: {ex}\n{ex.StackTrace}\n");
                File.AppendAllText(logPath, $"[{DateTime.Now}] Log file location: {logPath}\n");
                Console.WriteLine($"[{DateTime.Now}] FATAL ERROR: {ex.Message}");
                throw; // Re-throw to let the exception show in the console
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}

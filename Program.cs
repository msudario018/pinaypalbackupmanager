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
            AppDataPaths.MigrateKnownFiles();
            var logPath = AppDataPaths.GetPath("startup.log");
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
                // Check for Velopack operations FIRST
                if (args.Length > 0)
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now}] Velopack args detected: {string.Join(", ", args)}\n");
                    Console.WriteLine($"[{DateTime.Now}] Velopack args: {string.Join(", ", args)}");
                    
                    if (args[0].StartsWith("--velo"))
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now}] Velopack operation detected, initializing Velopack only\n");
                        Console.WriteLine($"[{DateTime.Now}] Velopack operation, initializing only");
                        
                        var vpApp = VelopackApp.Build();
                        vpApp.Run();
                        File.AppendAllText(logPath, $"[{DateTime.Now}] Velopack operation completed\n");
                        Console.WriteLine($"[{DateTime.Now}] Velopack operation completed");
                        return;
                    }
                }
                
                // Normal app startup
                File.AppendAllText(logPath, $"[{DateTime.Now}] Normal app startup, initializing Velopack...\n");
                Console.WriteLine($"[{DateTime.Now}] Normal app startup, initializing Velopack...");
                
                var vp = VelopackApp.Build();
                vp.Run();
                File.AppendAllText(logPath, $"[{DateTime.Now}] Velopack initialized successfully\n");
                Console.WriteLine($"[{DateTime.Now}] Velopack.Run() completed");

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

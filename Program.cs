using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace PinayPalBackupManager
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            // Setup DI
            var services = new ServiceCollection();
            services.AddSingleton<Services.BackupManager>();
            services.AddSingleton<UI.ViewModels.FtpViewModel>(sp => new UI.ViewModels.FtpViewModel(sp.GetRequiredService<Services.BackupManager>()));

            var provider = services.BuildServiceProvider();
            Services.ServiceLocator.Provider = provider;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;

namespace PinayPalBackupManager.Services
{
    public static class ThemeService
    {
        private static readonly string PrefFile = Path.Combine(AppDataPaths.CurrentDirectory, "theme.txt");
        private static bool _isApplying = false;

        public static bool IsDark { get; private set; } = true;

        public static event Action<bool>? OnThemeChanged;

        public static void Load()
        {
            try
            {
                if (File.Exists(PrefFile))
                    IsDark = File.ReadAllText(PrefFile).Trim() != "light";
            }
            catch { }
            Apply();
        }

        public static void Toggle()
        {
            if (_isApplying) return;
            
            IsDark = !IsDark;
            Apply();
            Save();
            OnThemeChanged?.Invoke(IsDark);
        }

        private static void Apply()
        {
            if (Application.Current == null || _isApplying) return;
            
            _isApplying = true;
            
            // Use dispatcher to ensure UI thread execution with minimal delay
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
                }
                finally
                {
                    _isApplying = false;
                }
            }, DispatcherPriority.Normal);
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                File.WriteAllText(PrefFile, IsDark ? "dark" : "light");
            }
            catch { }
        }
    }
}

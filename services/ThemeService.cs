using System;
using System.IO;
using Avalonia;
using Avalonia.Styling;

namespace PinayPalBackupManager.Services
{
    public static class ThemeService
    {
        private static readonly string PrefFile = Path.Combine(AppDataPaths.CurrentDirectory, "theme.txt");

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
            IsDark = !IsDark;
            Apply();
            Save();
            OnThemeChanged?.Invoke(IsDark);
        }

        private static void Apply()
        {
            if (Application.Current != null)
                Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
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

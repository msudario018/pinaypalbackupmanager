using System;
using System.IO;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;

namespace PinayPalBackupManager.Services
{
    public static class ThemeService
    {
        private static readonly string PrefFile = Path.Combine(AppDataPaths.CurrentDirectory, "theme.txt");
        private static bool _isApplying = false;

        public static bool IsDark { get; set; } = true;

        public static event Action<bool>? OnThemeChanged;

        public static void Load()
        {
            try
            {
                // Load basic dark/light preference
                if (File.Exists(PrefFile))
                    IsDark = File.ReadAllText(PrefFile).Trim() != "light";
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[THEME] Error loading theme: {ex.Message}", "Error", "SYSTEM");
            }
            
            Apply();
        }

        public static void Toggle()
        {
            if (_isApplying) return;
            
            IsDark = !IsDark;
            Apply();
            Save();
            try
            {
                OnThemeChanged?.Invoke(IsDark);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[THEME] Error in OnThemeChanged subscribers: {ex}", "Error", "SYSTEM");
            }
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
                    if (Application.Current == null) return;

                    Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[THEME] Error in Apply(): {ex}", "Error", "SYSTEM");
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
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[THEME] Error loading theme: {ex.Message}", "Error", "SYSTEM");
            }
        }

        public static void AutoCheckAndApply()
        {
            if (!ConfigService.Current.Operation.ThemeAutoSchedule) return;
            
            var hour = DateTime.Now.Hour;
            var darkHour = ConfigService.Current.Operation.ThemeDarkHour;
            var lightHour = ConfigService.Current.Operation.ThemeLightHour;
            
            bool shouldBeDark;
            if (darkHour > lightHour)
                shouldBeDark = hour >= darkHour || hour < lightHour;
            else
                shouldBeDark = hour >= darkHour && hour < lightHour;
            
            if (shouldBeDark != IsDark)
            {
                IsDark = shouldBeDark;
                Apply();
                Save();
                OnThemeChanged?.Invoke(IsDark);
            }
        }

    }
}

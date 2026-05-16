using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Media;
using System.Text.Json;

namespace PinayPalBackupManager.Services
{
    public static class ThemeService
    {
        private static readonly string PrefFile = Path.Combine(AppDataPaths.CurrentDirectory, "theme.txt");
        private static readonly string SettingsFile = Path.Combine(AppDataPaths.CurrentDirectory, "theme_settings.json");
        private static bool _isApplying = false;

        public static bool IsDark { get; private set; } = true;
        public static ThemeSettings CurrentSettings { get; private set; } = new ThemeSettings();

        public static event Action<bool>? OnThemeChanged;
        public static event Action<ThemeSettings>? OnCustomThemeChanged;

        public static void Load()
        {
            try
            {
                // Load basic dark/light preference
                if (File.Exists(PrefFile))
                    IsDark = File.ReadAllText(PrefFile).Trim() != "light";
                
                // Load custom theme settings
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    CurrentSettings = JsonSerializer.Deserialize<ThemeSettings>(json) ?? new ThemeSettings();
                }
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
            OnThemeChanged?.Invoke(IsDark);
        }

        public static void ApplyCustomTheme(ThemeSettings settings)
        {
            if (_isApplying) return;
            
            CurrentSettings = settings;
            Apply();
            SaveCustomSettings();
            OnCustomThemeChanged?.Invoke(settings);
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
                    // Apply dark/light theme
                    Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
                    
                    // Apply custom theme settings
                    ApplyCustomThemeSettings();
                }
                finally
                {
                    _isApplying = false;
                }
            }, DispatcherPriority.Normal);
        }

        private static void ApplyCustomThemeSettings()
        {
            try
            {
                var resources = Application.Current.Resources;
                
                // Apply accent colors
                resources["AccentWebsite"] = Color.Parse(CurrentSettings.PrimaryColor);
                resources["AccentMailchimp"] = Color.Parse(CurrentSettings.SecondaryColor);
                resources["AccentSQL"] = Color.Parse(CurrentSettings.PrimaryColor);
                resources["AccentError"] = Color.Parse(CurrentSettings.ErrorColor);
                resources["AccentSuccess"] = Color.Parse(CurrentSettings.SuccessColor);
                
                // Apply font settings
                resources["AppFontFamily"] = new FontFamily(CurrentSettings.FontFamily);
                resources["AppFontSize"] = CurrentSettings.FontSize;
                resources["AppFontWeight"] = GetFontWeight(CurrentSettings.FontWeight);
                
                // Apply UI scale
                resources["AppUIScale"] = CurrentSettings.UIScale / 100.0;
                resources["AppBorderRadius"] = CurrentSettings.BorderRadius;
                
                // Apply animation speed
                resources["AppAnimationSpeed"] = GetAnimationDuration(CurrentSettings.AnimationSpeed);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[THEME] Error applying custom theme settings: {ex.Message}", "", "Error", "SYSTEM");
            }
        }

        private static FontWeight GetFontWeight(string weight)
        {
            return weight switch
            {
                "Normal" => FontWeight.Normal,
                "Medium" => FontWeight.Medium,
                "SemiBold" => FontWeight.SemiBold,
                "Bold" => FontWeight.Bold,
                _ => FontWeight.Normal
            };
        }

        private static TimeSpan GetAnimationDuration(string speed)
        {
            return speed switch
            {
                "No Animations" => TimeSpan.Zero,
                "Fast" => TimeSpan.FromMilliseconds(150),
                "Normal" => TimeSpan.FromMilliseconds(250),
                "Slow" => TimeSpan.FromMilliseconds(400),
                _ => TimeSpan.FromMilliseconds(250)
            };
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
        
        private static void SaveCustomSettings()
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                var json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
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

        public static void ResetToDefault()
        {
            CurrentSettings = new ThemeSettings();
            Apply();
            SaveCustomSettings();
            OnCustomThemeChanged?.Invoke(CurrentSettings);
        }
    }

    public class ThemeSettings
    {
        public string Name { get; set; } = "Custom";
        public string PrimaryColor { get; set; } = "#52B788";
        public string SecondaryColor { get; set; } = "#00b4d8";
        public string ErrorColor { get; set; } = "#F38BA8";
        public string SuccessColor { get; set; } = "#A6E3A1";
        public string FontFamily { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 14;
        public string FontWeight { get; set; } = "Normal";
        public int UIScale { get; set; } = 100;
        public int BorderRadius { get; set; } = 8;
        public string AnimationSpeed { get; set; } = "Normal";
    }
}

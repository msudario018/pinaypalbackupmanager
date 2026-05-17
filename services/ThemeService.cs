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
            try
            {
                OnThemeChanged?.Invoke(IsDark);
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[THEME] Error in OnThemeChanged subscribers: {ex}", "Error", "SYSTEM");
            }
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
                    if (Application.Current == null) return;

                    // Apply dark/light theme
                    Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
                    
                    // Apply custom theme settings
                    ApplyCustomThemeSettings();
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

        private static void ApplyCustomThemeSettings()
        {
            try
            {
                var resources = Application.Current.Resources;
                
                // Parse colors
                var primary = Color.Parse(CurrentSettings.PrimaryColor);
                var secondary = Color.Parse(CurrentSettings.SecondaryColor);
                var error = Color.Parse(CurrentSettings.ErrorColor);
                var success = Color.Parse(CurrentSettings.SuccessColor);
                
                // Apply accent colors (must be SolidColorBrush to match XAML resource types)
                resources["AccentWebsite"] = new SolidColorBrush(primary);
                resources["AccentWebsiteLight"] = new SolidColorBrush(Lighten(primary, 0.25));
                resources["AccentWebsiteDark"] = new SolidColorBrush(Darken(primary, 0.15));
                resources["AccentWebsiteBg"] = new SolidColorBrush(WithAlpha(primary, 0.08));
                resources["AccentWebsiteMuted"] = new SolidColorBrush(Lighten(primary, 0.15));
                resources["AccentWebsiteSurface"] = new SolidColorBrush(Darken(primary, 0.1));
                resources["AccentWebsiteGlow"] = new SolidColorBrush(WithAlpha(primary, 0.2));
                
                resources["AccentMailchimp"] = new SolidColorBrush(secondary);
                resources["AccentMailchimpLight"] = new SolidColorBrush(Lighten(secondary, 0.25));
                resources["AccentMailchimpDark"] = new SolidColorBrush(Darken(secondary, 0.15));
                resources["AccentMailchimpBg"] = new SolidColorBrush(WithAlpha(secondary, 0.08));
                resources["AccentMailchimpMuted"] = new SolidColorBrush(Lighten(secondary, 0.15));
                resources["AccentMailchimpSurface"] = new SolidColorBrush(Darken(secondary, 0.1));
                resources["AccentMailchimpGlow"] = new SolidColorBrush(WithAlpha(secondary, 0.2));
                
                resources["AccentSql"] = new SolidColorBrush(primary);
                resources["AccentSqlLight"] = new SolidColorBrush(Lighten(primary, 0.25));
                resources["AccentSqlDark"] = new SolidColorBrush(Darken(primary, 0.15));
                resources["AccentSqlBg"] = new SolidColorBrush(WithAlpha(primary, 0.08));
                resources["AccentSqlMuted"] = new SolidColorBrush(Lighten(primary, 0.15));
                resources["AccentSqlSurface"] = new SolidColorBrush(Darken(primary, 0.1));
                resources["AccentSqlGlow"] = new SolidColorBrush(WithAlpha(primary, 0.2));
                
                resources["AccentError"] = new SolidColorBrush(error);
                resources["AccentErrorLight"] = new SolidColorBrush(Lighten(error, 0.25));
                resources["AccentErrorDark"] = new SolidColorBrush(Darken(error, 0.15));
                resources["AccentErrorBg"] = new SolidColorBrush(WithAlpha(error, 0.08));
                resources["AccentErrorMuted"] = new SolidColorBrush(Lighten(error, 0.15));
                resources["AccentErrorSurface"] = new SolidColorBrush(Darken(error, 0.1));
                resources["AccentErrorGlow"] = new SolidColorBrush(WithAlpha(error, 0.2));
                
                resources["AccentSuccess"] = new SolidColorBrush(success);
                resources["AccentInfo"] = new SolidColorBrush(secondary);
                resources["AccentInfoLight"] = new SolidColorBrush(Lighten(secondary, 0.25));
                
                // Apply font settings (must be exact types Avalonia expects)
                resources["AppFontFamily"] = new FontFamily(CurrentSettings.FontFamily);
                resources["AppFontSize"] = (double)CurrentSettings.FontSize;
                resources["AppFontWeight"] = GetFontWeight(CurrentSettings.FontWeight);
                
                // Apply UI scale (cast to double to avoid InvalidCastException Int32->Double)
                resources["AppUIScale"] = (double)CurrentSettings.UIScale / 100.0;
                resources["AppBorderRadius"] = (double)CurrentSettings.BorderRadius;
                
                // Apply animation speed
                resources["AppAnimationSpeed"] = GetAnimationDuration(CurrentSettings.AnimationSpeed);
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[THEME] Error applying custom theme settings: {ex.Message}", "", "Error", "SYSTEM");
            }
        }
        
        private static Color Lighten(Color c, double amount)
        {
            return new Color(
                c.A,
                (byte)Math.Min(255, c.R + (255 - c.R) * amount),
                (byte)Math.Min(255, c.G + (255 - c.G) * amount),
                (byte)Math.Min(255, c.B + (255 - c.B) * amount));
        }
        
        private static Color Darken(Color c, double amount)
        {
            return new Color(
                c.A,
                (byte)Math.Max(0, c.R * (1 - amount)),
                (byte)Math.Max(0, c.G * (1 - amount)),
                (byte)Math.Max(0, c.B * (1 - amount)));
        }
        
        private static Color WithAlpha(Color c, double alpha)
        {
            return new Color((byte)(c.A * alpha), c.R, c.G, c.B);
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

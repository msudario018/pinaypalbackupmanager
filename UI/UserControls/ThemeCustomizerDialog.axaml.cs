using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class ThemeCustomizerDialog : UserControl
    {
        private readonly List<ColorPreset> _presets = new()
        {
            new("Midnight",   "#52B788", "#00b4d8", "#F38BA8", "#A6E3A1"),
            new("Ocean",      "#3B82F6", "#06B6D4", "#EF4444", "#22C55E"),
            new("Sunset",     "#F59E0B", "#F97316", "#EF4444", "#84CC16"),
            new("Forest",     "#059669", "#10B981", "#DC2626", "#22C55E"),
            new("Berry",      "#D946EF", "#8B5CF6", "#EF4444", "#84CC16"),
            new("Mono",       "#6B7280", "#9CA3AF", "#EF4444", "#22C55E"),
            new("Rose",       "#E11D48", "#FB7185", "#DC2626", "#22C55E"),
            new("Cobalt",     "#2563EB", "#60A5FA", "#EF4444", "#22C55E"),
        };

        public ThemeCustomizerDialog()
        {
            InitializeComponent();
            LoadCurrentSettings();
            BuildPresetButtons();
            WireEvents();
            RefreshPreview();
        }

        private void WireEvents()
        {
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => CloseDialog();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => CloseDialog();
            this.FindControl<Button>("BtnApply")!.Click += (_, _) => ApplySettings();
            this.FindControl<Button>("BtnReset")!.Click += (_, _) => ResetToDefault();

            this.FindControl<TextBox>("TxtPrimary")!.TextChanged += (_, _) => { UpdateSwatch("SwatchPrimary", "TxtPrimary"); RefreshPreview(); };
            this.FindControl<TextBox>("TxtSecondary")!.TextChanged += (_, _) => { UpdateSwatch("SwatchSecondary", "TxtSecondary"); RefreshPreview(); };
            this.FindControl<TextBox>("TxtError")!.TextChanged += (_, _) => { UpdateSwatch("SwatchError", "TxtError"); RefreshPreview(); };
            this.FindControl<TextBox>("TxtSuccess")!.TextChanged += (_, _) => { UpdateSwatch("SwatchSuccess", "TxtSuccess"); RefreshPreview(); };
            this.FindControl<TextBox>("TxtFontFamily")!.TextChanged += (_, _) => RefreshPreview();

            var slider = this.FindControl<Slider>("SliderFontSize");
            if (slider != null)
            {
                slider.ValueChanged += (_, e) =>
                {
                    var lbl = this.FindControl<TextBlock>("LblFontSize");
                    if (lbl != null) lbl.Text = $"{(int)e.NewValue} px";
                    RefreshPreview();
                };
            }
        }

        private void LoadCurrentSettings()
        {
            var s = ThemeService.CurrentSettings;
            SetText("TxtPrimary", s.PrimaryColor);
            SetText("TxtSecondary", s.SecondaryColor);
            SetText("TxtError", s.ErrorColor);
            SetText("TxtSuccess", s.SuccessColor);
            SetText("TxtFontFamily", s.FontFamily);

            var slider = this.FindControl<Slider>("SliderFontSize");
            if (slider != null) slider.Value = s.FontSize;

            UpdateSwatch("SwatchPrimary", "TxtPrimary");
            UpdateSwatch("SwatchSecondary", "TxtSecondary");
            UpdateSwatch("SwatchError", "TxtError");
            UpdateSwatch("SwatchSuccess", "TxtSuccess");
        }

        private void SetText(string name, string text)
        {
            var tb = this.FindControl<TextBox>(name);
            if (tb != null) tb.Text = text;
        }

        private string GetText(string name)
        {
            var tb = this.FindControl<TextBox>(name);
            return tb?.Text?.Trim() ?? "";
        }

        private void BuildPresetButtons()
        {
            var panel = this.FindControl<WrapPanel>("PresetPanel");
            if (panel == null) return;

            foreach (var p in _presets)
            {
                var btn = new Button
                {
                    Content = p.Name,
                    Background = Brush.Parse(p.PrimaryColor),
                    Foreground = Brush.Parse("#FFFFFF"),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 6),
                    Margin = new Thickness(0, 0, 8, 8),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                btn.Click += (_, _) => ApplyPreset(p);
                panel.Children.Add(btn);
            }
        }

        private void ApplyPreset(ColorPreset p)
        {
            SetText("TxtPrimary", p.PrimaryColor);
            SetText("TxtSecondary", p.SecondaryColor);
            SetText("TxtError", p.ErrorColor);
            SetText("TxtSuccess", p.SuccessColor);

            UpdateSwatch("SwatchPrimary", "TxtPrimary");
            UpdateSwatch("SwatchSecondary", "TxtSecondary");
            UpdateSwatch("SwatchError", "TxtError");
            UpdateSwatch("SwatchSuccess", "TxtSuccess");

            RefreshPreview();
        }

        private void UpdateSwatch(string swatchName, string textBoxName)
        {
            var swatch = this.FindControl<Border>(swatchName);
            var txt = GetText(textBoxName);
            if (swatch != null)
            {
                try { swatch.Background = Brush.Parse(txt); }
                catch { swatch.Background = Brushes.Transparent; }
            }
        }

        private void RefreshPreview()
        {
            var primary = GetText("TxtPrimary");
            var font = GetText("TxtFontFamily");
            var size = (int)(this.FindControl<Slider>("SliderFontSize")?.Value ?? 14);

            var previewTitle = this.FindControl<TextBlock>("PreviewTitle");
            var previewBody = this.FindControl<TextBlock>("PreviewBody");
            var previewBtn = this.FindControl<Button>("PreviewBtn");

            if (previewTitle != null)
            {
                previewTitle.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(font) ? "Segoe UI" : font);
                previewTitle.FontSize = size + 2;
            }
            if (previewBody != null)
            {
                previewBody.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(font) ? "Segoe UI" : font);
                previewBody.FontSize = size;
            }
            if (previewBtn != null)
            {
                try { previewBtn.Background = Brush.Parse(primary); }
                catch { previewBtn.Background = Brush.Parse("#52B788"); }
                previewBtn.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(font) ? "Segoe UI" : font);
                previewBtn.FontSize = size;
            }
        }

        private void ApplySettings()
        {
            var settings = new ThemeSettings
            {
                Name = "Custom",
                PrimaryColor = ValidateHex(GetText("TxtPrimary"), "#52B788"),
                SecondaryColor = ValidateHex(GetText("TxtSecondary"), "#00b4d8"),
                ErrorColor = ValidateHex(GetText("TxtError"), "#F38BA8"),
                SuccessColor = ValidateHex(GetText("TxtSuccess"), "#A6E3A1"),
                FontFamily = string.IsNullOrWhiteSpace(GetText("TxtFontFamily")) ? "Segoe UI" : GetText("TxtFontFamily"),
                FontSize = (int)(this.FindControl<Slider>("SliderFontSize")?.Value ?? 14),
            };

            ThemeService.ApplyCustomTheme(settings);
            CloseDialog();
        }

        private void ResetToDefault()
        {
            ThemeService.ResetToDefault();
            LoadCurrentSettings();
            RefreshPreview();
        }

        private static string ValidateHex(string input, string fallback)
        {
            var trimmed = input.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return fallback;
            if (!trimmed.StartsWith("#")) trimmed = "#" + trimmed;
            try { _ = Color.Parse(trimmed); return trimmed; }
            catch { return fallback; }
        }

        private void CloseDialog()
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            parentWindow?.Close();
        }

        private record ColorPreset(string Name, string PrimaryColor, string SecondaryColor, string ErrorColor, string SuccessColor);
    }
}

using Avalonia.Controls;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public class LocalizedTextBlock : TextBlock
    {
        private string _key = string.Empty;
        
        public string Key
        {
            get => _key;
            set
            {
                _key = value;
                UpdateText();
            }
        }

        public LocalizedTextBlock()
        {
            LocalizationService.OnLanguageChanged += UpdateText;
            UpdateText();
        }

        private void UpdateText()
        {
            if (!string.IsNullOrEmpty(_key))
            {
                var translated = LocalizationService.Get(_key);
                Text = translated;
                // Debug: Log translation
                System.Diagnostics.Debug.WriteLine($"[Localize] {_key} -> {translated} (lang: {LocalizationService.CurrentLanguage})");
            }
        }
    }
}

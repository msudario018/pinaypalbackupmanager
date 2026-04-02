using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.ComponentModel;
using System.IO;

namespace PinayPalBackupManager.UI.UserControls
{
    [DesignTimeVisible(true)]
    public partial class UploadAvatarDialog : UserControl
    {
        public event EventHandler<string>? OnAvatarUploaded;
        public event EventHandler? OnCancel;

        private string? _selectedFilePath;

        public UploadAvatarDialog()
        {
            InitializeComponent();
            
            var btnCancel = this.FindControl<Button>("BtnCancel");
            var btnBrowse = this.FindControl<Button>("BtnBrowse");
            var btnUpload = this.FindControl<Button>("BtnUpload");
            
            if (btnCancel != null) btnCancel.Click += (s, e) => OnCancel?.Invoke(this, EventArgs.Empty);
            if (btnBrowse != null) btnBrowse.Click += OnBrowseClick;
            if (btnUpload != null) btnUpload.Click += OnUploadClick;
        }

        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            var txtFilePath = this.FindControl<TextBox>("TxtFilePath");
            var txtError = this.FindControl<TextBlock>("TxtError");
            
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Avatar Image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Image Files") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                _selectedFilePath = files[0].Path.LocalPath;
                if (txtFilePath != null) txtFilePath.Text = Path.GetFileName(_selectedFilePath);
                
                // Validate file size (2MB limit)
                var fileInfo = new FileInfo(_selectedFilePath);
                if (fileInfo.Length > 2 * 1024 * 1024)
                {
                    if (txtError != null) txtError.Text = "File size must be less than 2MB.";
                    _selectedFilePath = null;
                    if (txtFilePath != null) txtFilePath.Text = string.Empty;
                    return;
                }
                
                // Update preview (simplified - would need actual image loading)
                if (txtError != null)
                {
                    txtError.Text = "Image selected successfully!";
                    txtError.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                }
            }
        }

        private void OnUploadClick(object? sender, RoutedEventArgs e)
        {
            var txtError = this.FindControl<TextBlock>("TxtError");
            
            if (string.IsNullOrEmpty(_selectedFilePath))
            {
                if (txtError != null)
                {
                    txtError.Text = "Please select an image file.";
                    txtError.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                }
                return;
            }

            // For now, just notify parent with the file path
            // In production, would upload to server or save to user profile
            OnAvatarUploaded?.Invoke(this, _selectedFilePath!);
        }
    }
}

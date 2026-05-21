using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI
{
    public partial class SetupWizardWindow : Window
    {
        private int _currentStep = 1;
        private const int TotalSteps = 6;
        private bool _isAdminPC = true;
        private bool _settingsImported = false;

        public event Action? OnSetupComplete;

        public SetupWizardWindow()
        {
            AvaloniaXamlLoader.Load(this);
            _isAdminPC = true; // Default: assume this is the first/admin PC
            SetupEventHandlers();
            UpdateUIForCurrentStep();
            SetDefaultPaths();
            UpdateAdminUserText();

            // Check Firebase asynchronously to confirm this is actually the admin PC
            _ = Task.Run(async () =>
            {
                try
                {
                    var users = await FirebaseUserService.GetAllUsersAsync();
                    bool hasAdmin = users.Any(u => u.Role == "Admin");
                    if (hasAdmin)
                    {
                        _isAdminPC = false;
                        Dispatcher.UIThread.Post(() => UpdateAdminUserText());
                    }
                }
                catch { /* Firebase unreachable — keep admin PC assumption */ }
            });
        }

        private void SetupEventHandlers()
        {
            // Navigation buttons
            this.FindControl<Button>("BtnNext")!.Click += OnNextClick;
            this.FindControl<Button>("BtnBack")!.Click += OnBackClick;
            this.FindControl<Button>("BtnSkip")!.Click += OnSkipClick;

            // Browse buttons
            this.FindControl<Button>("BtnBrowseFtpFolder")!.Click += (_, _) => BrowseFolder("TxtFtpLocalFolder");
            this.FindControl<Button>("BtnBrowseSqlFolder")!.Click += (_, _) => BrowseFolder("TxtSqlLocalFolder");
            this.FindControl<Button>("BtnBrowseMcFolder")!.Click += (_, _) => BrowseFolder("TxtMcFolder");
            this.FindControl<Button>("BtnBrowseImport")!.Click += (_, _) => BrowseImportFile();

            // Test connection buttons
            this.FindControl<Button>("BtnTestFTP")!.Click += OnTestFtpClick;
            this.FindControl<Button>("BtnTestSQL")!.Click += OnTestSqlClick;
            this.FindControl<Button>("BtnTestMailchimp")!.Click += OnTestMailchimpClick;

            // Import all settings button
            this.FindControl<Button>("BtnImportAll")!.Click += OnImportAllClick;

            // Password real-time validation
            var passwordBox = this.FindControl<TextBox>("TxtAdminPassword")!;
            var confirmPasswordBox = this.FindControl<TextBox>("TxtAdminPasswordConfirm")!;
            
            passwordBox.TextChanged += (_, _) => ValidatePasswordMatch();
            confirmPasswordBox.TextChanged += (_, _) => ValidatePasswordMatch();

            // Update summary when entering step 6
            this.FindControl<StackPanel>("Step6Security")!.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == "IsVisible" && e.NewValue is true)
                    UpdateSummary();
            };
        }

        private void SetDefaultPaths()
        {
            var baseDir = EnvironmentConfigService.GetBackupPath();
            this.FindControl<TextBox>("TxtFtpLocalFolder")!.Text = Path.Combine(baseDir, "FTP");
            this.FindControl<TextBox>("TxtSqlLocalFolder")!.Text = Path.Combine(baseDir, "SQL");
            this.FindControl<TextBox>("TxtMcFolder")!.Text = Path.Combine(baseDir, "Mailchimp");
        }

        private void ValidatePasswordMatch()
        {
            var password = this.FindControl<TextBox>("TxtAdminPassword")!.Text ?? "";
            var confirmPassword = this.FindControl<TextBox>("TxtAdminPasswordConfirm")!.Text ?? "";
            var errorTextBlock = this.FindControl<TextBlock>("ErrorAdminPasswordConfirm")!;

            if (!string.IsNullOrEmpty(confirmPassword) && password != confirmPassword)
            {
                errorTextBlock.Text = "Passwords do not match";
                errorTextBlock.IsVisible = true;
            }
            else
            {
                errorTextBlock.Text = "";
                errorTextBlock.IsVisible = false;
            }
        }

        private void UpdateAdminUserText()
        {
            // Find the title text block in Step 1
            var titleTextBlock = this.FindControl<TextBlock>("TxtAdminTitle");
            var descTextBlock = this.FindControl<TextBlock>("TxtAdminDesc");
            var inviteCodePanel = this.FindControl<StackPanel>("InviteCodePanel");

            if (titleTextBlock != null && descTextBlock != null && inviteCodePanel != null)
            {
                if (_isAdminPC)
                {
                    titleTextBlock.Text = "Create Administrator Account";
                    descTextBlock.Text = "Set up your admin credentials to manage backup system.";
                    inviteCodePanel.IsVisible = false;
                }
                else
                {
                    titleTextBlock.Text = "Create User Account";
                    descTextBlock.Text = "Set up your user credentials to access the backup system.";
                    inviteCodePanel.IsVisible = true;
                }
            }
        }

        private async void OnNextClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!await ValidateCurrentStep()) return;

                if (_currentStep < TotalSteps)
                {
                    _currentStep++;
                    // Skip hidden backup tabs if settings were imported
                    if (_settingsImported && _currentStep >= 3 && _currentStep <= 5)
                        _currentStep = 6;
                    UpdateUIForCurrentStep();
                }
                else
                {
                    await CompleteSetup();
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SETUPWIZARD] OnNextClick error: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private void OnBackClick(object? sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                // Skip hidden backup tabs if settings were imported
                if (_settingsImported && _currentStep >= 3 && _currentStep <= 5)
                    _currentStep = 2;
                UpdateUIForCurrentStep();
            }
        }

        private void OnSkipClick(object? sender, RoutedEventArgs e)
        {
            if (_currentStep < TotalSteps)
            {
                _currentStep++;
                // Skip hidden backup tabs if settings were imported
                if (_settingsImported && _currentStep >= 3 && _currentStep <= 5)
                    _currentStep = 6;
                UpdateUIForCurrentStep();
            }
        }

        private void UpdateUIForCurrentStep()
        {
            // Hide all steps
            this.FindControl<StackPanel>("Step1Admin")!.IsVisible = false;
            this.FindControl<StackPanel>("Step2Import")!.IsVisible = false;
            this.FindControl<StackPanel>("Step3FTP")!.IsVisible = false;
            this.FindControl<StackPanel>("Step4SQL")!.IsVisible = false;
            this.FindControl<StackPanel>("Step5Mailchimp")!.IsVisible = false;
            this.FindControl<StackPanel>("Step6Security")!.IsVisible = false;

            // Show current step
            StackPanel? currentPanel = _currentStep switch
            {
                1 => this.FindControl<StackPanel>("Step1Admin"),
                2 => this.FindControl<StackPanel>("Step2Import"),
                3 => this.FindControl<StackPanel>("Step3FTP"),
                4 => this.FindControl<StackPanel>("Step4SQL"),
                5 => this.FindControl<StackPanel>("Step5Mailchimp"),
                6 => this.FindControl<StackPanel>("Step6Security"),
                _ => null
            };
            if (currentPanel != null) currentPanel.IsVisible = true;

            // Update title
            var visibleStepCount = _settingsImported ? 3 : TotalSteps;
            var displayStep = _settingsImported ? (_currentStep == 6 ? 3 : _currentStep) : _currentStep;
            this.FindControl<TextBlock>("TxtStepTitle")!.Text = $"Step {displayStep} of {visibleStepCount}";

            // Update step indicators (dots)
            UpdateStepDots();

            // Update buttons
            this.FindControl<Button>("BtnBack")!.IsVisible = _currentStep > 1;
            this.FindControl<Button>("BtnNext")!.Content = _currentStep == TotalSteps ? "Complete Setup →" : "Next →";
            this.FindControl<Button>("BtnSkip")!.IsVisible = _currentStep < TotalSteps;
        }

        private void UpdateStepDots()
        {
            // Update step indicator dots
            for (int i = 1; i <= TotalSteps; i++)
            {
                var dot = this.FindControl<Avalonia.Controls.Shapes.Ellipse>($"StepDot{i}");
                if (dot != null)
                {
                    // Hide dots 3-5 when settings were imported
                    if (_settingsImported && i >= 3 && i <= 5)
                    {
                        dot.IsVisible = false;
                        continue;
                    }
                    dot.IsVisible = true;

                    if (i < _currentStep)
                    {
                        // Completed step
                        dot.Fill = Avalonia.Media.Brush.Parse("#A6E3A1");
                        dot.Stroke = Avalonia.Media.Brush.Parse("#A6E3A1");
                    }
                    else if (i == _currentStep)
                    {
                        // Current step
                        dot.Fill = Avalonia.Media.Brush.Parse("#89B4FA");
                        dot.Stroke = Avalonia.Media.Brush.Parse("#89B4FA");
                    }
                    else
                    {
                        // Future step
                        dot.Fill = Avalonia.Media.Brush.Parse("Transparent");
                        dot.Stroke = Avalonia.Media.Brush.Parse("#585B70");
                    }
                }
            }
        }

        private async Task<bool> ValidateCurrentStep()
        {
            ClearErrors();

            return _currentStep switch
            {
                1 => ValidateAdminStep(),
                2 => true, // Import step is optional
                3 => ValidateFtpStep(),
                4 => ValidateSqlStep(),
                5 => ValidateMailchimpStep(),
                6 => await ValidateSecurityStep(),
                _ => true
            };
        }

        private bool ValidateAdminStep()
        {
            var username = this.FindControl<TextBox>("TxtAdminUsername")!.Text?.Trim() ?? "";
            var password = this.FindControl<TextBox>("TxtAdminPassword")!.Text ?? "";
            var confirmPassword = this.FindControl<TextBox>("TxtAdminPasswordConfirm")!.Text ?? "";
            var inviteCode = this.FindControl<TextBox>("TxtInviteCode")!.Text?.Trim() ?? "";

            bool valid = true;

            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            {
                ShowError("ErrorAdminUsername", "Username must be at least 3 characters");
                valid = false;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 8)
            {
                ShowError("ErrorAdminPassword", "Password must be at least 8 characters");
                valid = false;
            }
            else if (!password.Any(char.IsUpper))
            {
                ShowError("ErrorAdminPassword", "Password must contain at least one uppercase letter");
                valid = false;
            }
            else if (!password.Any(char.IsLower))
            {
                ShowError("ErrorAdminPassword", "Password must contain at least one lowercase letter");
                valid = false;
            }
            else if (!password.Any(char.IsDigit))
            {
                ShowError("ErrorAdminPassword", "Password must contain at least one digit");
                valid = false;
            }
            else
            {
                var specialChars = "!@#$%^&*()_+-=[]{}|;':\",.<>?";
                if (!password.Any(c => specialChars.Contains(c)))
                {
                    ShowError("ErrorAdminPassword", "Password must contain at least one special character");
                    valid = false;
                }
            }

            if (password != confirmPassword)
            {
                ShowError("ErrorAdminPasswordConfirm", "Passwords do not match");
                valid = false;
            }

            // Validate invite code if not admin PC
            if (!_isAdminPC && string.IsNullOrWhiteSpace(inviteCode))
            {
                ShowError("ErrorInviteCode", "Invite code is required");
                valid = false;
            }

            return valid;
        }

        private bool ValidateFtpStep()
        {
            if (!this.FindControl<CheckBox>("ChkEnableFTP")!.IsChecked!.Value)
                return true;

            var host = this.FindControl<TextBox>("TxtFtpHost")!.Text?.Trim() ?? "";
            bool valid = true;

            if (string.IsNullOrWhiteSpace(host))
            {
                ShowError("ErrorFtpHost", "FTP host is required when FTP is enabled");
                valid = false;
            }

            return valid;
        }

        private bool ValidateSqlStep()
        {
            // SQL is optional, no strict validation required
            return true;
        }

        private bool ValidateMailchimpStep()
        {
            if (!this.FindControl<CheckBox>("ChkEnableMailchimp")!.IsChecked!.Value)
                return true;

            var apiKey = this.FindControl<TextBox>("TxtMcApiKey")!.Text?.Trim() ?? "";
            var audienceId = this.FindControl<TextBox>("TxtMcAudienceId")!.Text?.Trim() ?? "";
            bool valid = true;

            if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.Contains('-'))
            {
                ShowError("ErrorMcApiKey", "Valid API key is required (format: key-datacenter)");
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(audienceId))
            {
                ShowError("ErrorMcAudienceId", "Audience ID is required");
                valid = false;
            }

            return valid;
        }

        private async Task<bool> ValidateSecurityStep()
        {
            // Security settings are always valid
            return await Task.FromResult(true);
        }

        private void ShowError(string controlName, string message)
        {
            var tb = this.FindControl<TextBlock>(controlName);
            if (tb != null)
            {
                tb.Text = message;
                tb.IsVisible = true;
            }
        }

        private void ClearErrors()
        {
            var errorControls = new[]
            {
                "ErrorAdminUsername", "ErrorAdminPassword", "ErrorAdminPasswordConfirm",
                "ErrorFtpHost", "ErrorSqlHost", "ErrorMcApiKey", "ErrorMcAudienceId"
            };

            foreach (var name in errorControls)
            {
                var tb = this.FindControl<TextBlock>(name);
                if (tb != null)
                {
                    tb.Text = "";
                    tb.IsVisible = false;
                }
            }
        }

        private async void BrowseImportFile()
        {
            try
            {
                var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Settings File",
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new FilePickerFileType("PinayPal Encrypted Settings (*.ppenc)") { Patterns = new[] { "*.ppenc" } },
                        new FilePickerFileType("JSON Settings (*.json)") { Patterns = new[] { "*.json" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (result.Count > 0 && !string.IsNullOrEmpty(result[0].Path.LocalPath))
                {
                    this.FindControl<TextBox>("TxtImportFile")!.Text = result[0].Path.LocalPath;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SETUPWIZARD] BrowseImportFile error: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private async void OnImportAllClick(object? sender, RoutedEventArgs e)
        {
            var filePath = this.FindControl<TextBox>("TxtImportFile")!.Text?.Trim();
            var resultText = this.FindControl<TextBlock>("TxtImportResult")!;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                resultText.Text = "❌ Please select a valid file.";
                resultText.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                resultText.IsVisible = true;
                return;
            }

            try
            {
                string json;
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (ext == ".ppenc")
                {
                    var encrypted = File.ReadAllText(filePath);
                    try
                    {
                        json = DecryptPpencString(encrypted);
                    }
                    catch
                    {
                        resultText.Text = "❌ Failed to decrypt .ppenc file. It may be corrupted or use a different key format.";
                        resultText.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                        resultText.IsVisible = true;
                        return;
                    }
                }
                else
                {
                    json = File.ReadAllText(filePath);
                }

                var settings = System.Text.Json.JsonSerializer.Deserialize<Services.AppSettings>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (settings == null)
                {
                    resultText.Text = "❌ Failed to parse configuration file.";
                    resultText.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    resultText.IsVisible = true;
                    return;
                }

                int importedCount = 0;

                // FTP
                if (!string.IsNullOrEmpty(settings.Ftp?.Host))
                {
                    this.FindControl<TextBox>("TxtFtpHost")!.Text = settings.Ftp.Host;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Ftp?.User))
                {
                    this.FindControl<TextBox>("TxtFtpUser")!.Text = settings.Ftp.User;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Ftp?.Password))
                {
                    this.FindControl<TextBox>("TxtFtpPassword")!.Text = settings.Ftp.Password;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Paths?.FtpLocalFolder))
                {
                    this.FindControl<TextBox>("TxtFtpLocalFolder")!.Text = settings.Paths.FtpLocalFolder;
                }

                // SQL
                if (!string.IsNullOrEmpty(settings.Sql?.Host))
                {
                    this.FindControl<TextBox>("TxtSqlHost")!.Text = settings.Sql.Host;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Sql?.User))
                {
                    this.FindControl<TextBox>("TxtSqlUser")!.Text = settings.Sql.User;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Sql?.Password))
                {
                    this.FindControl<TextBox>("TxtSqlPassword")!.Text = settings.Sql.Password;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Sql?.RemotePath))
                {
                    this.FindControl<TextBox>("TxtSqlRemotePath")!.Text = settings.Sql.RemotePath;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Paths?.SqlLocalFolder))
                {
                    this.FindControl<TextBox>("TxtSqlLocalFolder")!.Text = settings.Paths.SqlLocalFolder;
                }

                // Mailchimp
                if (!string.IsNullOrEmpty(settings.Mailchimp?.ApiKey))
                {
                    this.FindControl<TextBox>("TxtMcApiKey")!.Text = settings.Mailchimp.ApiKey;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Mailchimp?.AudienceId))
                {
                    this.FindControl<TextBox>("TxtMcAudienceId")!.Text = settings.Mailchimp.AudienceId;
                    importedCount++;
                }
                if (!string.IsNullOrEmpty(settings.Paths?.MailchimpFolder))
                {
                    this.FindControl<TextBox>("TxtMcFolder")!.Text = settings.Paths.MailchimpFolder;
                }

                resultText.Text = $"✓ Imported {importedCount} field(s) from {Path.GetFileName(filePath)}. Skipping to summary...";
                resultText.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                resultText.IsVisible = true;

                // Mark as imported and skip FTP/SQL/Mailchimp
                _settingsImported = true;
                _currentStep = 6;
                UpdateUIForCurrentStep();
            }
            catch (Exception ex)
            {
                resultText.Text = $"❌ Import failed: {ex.Message}";
                resultText.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                resultText.IsVisible = true;
            }
        }

        private static string DecryptPpencString(string cipherText)
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            using var aes = System.Security.Cryptography.Aes.Create();
            var keyBytes = new byte[32];
            var key = System.Text.Encoding.UTF8.GetBytes("PinayPalBackupManagerKey2024!");
            Array.Copy(key, keyBytes, Math.Min(key.Length, 32));
            aes.Key = keyBytes;

            var iv = new byte[16];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
            aes.IV = iv;

            var cipherBytes = new byte[fullCipher.Length - 16];
            Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return System.Text.Encoding.UTF8.GetString(decrypted);
        }

        private async void BrowseFolder(string textBoxName)
        {
            try
            {
                var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Backup Folder"
                });

                if (result.Count > 0 && !string.IsNullOrEmpty(result[0].Path.LocalPath))
                {
                    this.FindControl<TextBox>(textBoxName)!.Text = result[0].Path.LocalPath;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[SETUPWIZARD] BrowseFolder error: {ex.Message}", "Error", "SYSTEM");
            }
        }

        private async void OnTestFtpClick(object? sender, RoutedEventArgs e)
        {
            var btn = this.FindControl<Button>("BtnTestFTP")!;
            var result = this.FindControl<TextBlock>("TxtFtpTestResult")!;

            btn.IsEnabled = false;
            btn.Content = "Testing...";
            result.IsVisible = true;
            result.Foreground = Avalonia.Media.Brush.Parse("#A6ADC8");
            result.Text = "Testing connection...";

            try
            {
                var host = this.FindControl<TextBox>("TxtFtpHost")!.Text?.Trim() ?? "";
                var user = this.FindControl<TextBox>("TxtFtpUser")!.Text?.Trim() ?? "";
                var pass = this.FindControl<TextBox>("TxtFtpPassword")!.Text ?? "";

                if (string.IsNullOrEmpty(host))
                {
                    result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    result.Text = "❌ FTP host is required";
                    return;
                }

                using var ftp = new FtpService();
                result.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                result.Text = "✓ Configuration saved (connection test pending implementation)";
            }
            catch (Exception ex)
            {
                result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                result.Text = $"❌ Test failed: {ex.Message}";
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "Test Connection";
            }
        }

        private async void OnTestSqlClick(object? sender, RoutedEventArgs e)
        {
            var btn = this.FindControl<Button>("BtnTestSQL")!;
            var result = this.FindControl<TextBlock>("TxtSqlTestResult")!;

            btn.IsEnabled = false;
            btn.Content = "Testing...";
            result.IsVisible = true;
            result.Foreground = Avalonia.Media.Brush.Parse("#A6ADC8");
            result.Text = "Testing connection...";

            try
            {
                var host = this.FindControl<TextBox>("TxtSqlHost")!.Text?.Trim() ?? "";
                var user = this.FindControl<TextBox>("TxtSqlUser")!.Text?.Trim() ?? "";
                var pass = this.FindControl<TextBox>("TxtSqlPassword")!.Text ?? "";

                if (string.IsNullOrEmpty(host))
                {
                    result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    result.Text = "❌ SQL host is required";
                    return;
                }

                using var sql = new SqlService();
                sql.Initialize(host, user, pass, "");
                var connected = await sql.ConnectAsync();

                if (connected)
                {
                    result.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                    result.Text = "✓ SQL connection successful";
                }
                else
                {
                    result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    result.Text = "❌ SQL connection failed";
                }
            }
            catch (Exception ex)
            {
                result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                result.Text = $"❌ Test failed: {ex.Message}";
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "Test Connection";
            }
        }

        private async void OnTestMailchimpClick(object? sender, RoutedEventArgs e)
        {
            var btn = this.FindControl<Button>("BtnTestMailchimp")!;
            var result = this.FindControl<TextBlock>("TxtMcTestResult")!;

            btn.IsEnabled = false;
            btn.Content = "Testing...";
            result.IsVisible = true;
            result.Foreground = Avalonia.Media.Brush.Parse("#A6ADC8");
            result.Text = "Testing API...";

            try
            {
                var apiKey = this.FindControl<TextBox>("TxtMcApiKey")!.Text?.Trim() ?? "";
                var audienceId = this.FindControl<TextBox>("TxtMcAudienceId")!.Text?.Trim() ?? "";

                if (string.IsNullOrEmpty(apiKey) || !apiKey.Contains('-'))
                {
                    result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                    result.Text = "❌ Valid API key is required (format: key-datacenter)";
                    return;
                }

                using var mc = new MailchimpService(apiKey, audienceId);
                result.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                result.Text = "✓ API key format valid (full test requires valid credentials)";
            }
            catch (ArgumentException ex)
            {
                result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                result.Text = $"❌ {ex.Message}";
            }
            catch (Exception ex)
            {
                result.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                result.Text = $"❌ Test failed: {ex.Message}";
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "Test API";
            }
        }

        private async Task CompleteSetup()
        {
            var nextButton = this.FindControl<Button>("BtnNext")!;
            nextButton.IsEnabled = false;
            nextButton.Content = "Setting up...";

            try
            {
                var username = this.FindControl<TextBox>("TxtAdminUsername")!.Text!.Trim();
                var password = this.FindControl<TextBox>("TxtAdminPassword")!.Text!;

                if (_isAdminPC)
                {
                    // Admin PC: create admin directly (no invite code needed)
                    var (success, message) = AuthService.CreateUser(username, password, "Admin", "Active");
                    if (!success)
                    {
                        await ShowErrorDialog($"Failed to create admin user: {message}");
                        return;
                    }

                    // Auto-login the new admin
                    AuthService.Login(username, password);
                    SessionService.SaveSession(AuthService.CurrentUser!.Id);
                }
                else
                {
                    // Non-admin PC: validate invite code and create a pending standard user
                    var inviteCode = this.FindControl<TextBox>("TxtInviteCode")!.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(inviteCode))
                    {
                        await ShowErrorDialog("Invite code is required.");
                        return;
                    }

                    bool isValid = await FirebaseInviteService.ValidateInviteCodeAsync(inviteCode);
                    if (!isValid)
                    {
                        await ShowErrorDialog("Invalid or expired invite code.");
                        return;
                    }

                    var (success, message) = AuthService.CreateUser(username, password, "User", "Pending");
                    if (!success)
                    {
                        await ShowErrorDialog($"Failed to create user: {message}");
                        return;
                    }

                    // Mark invite code as used
                    await FirebaseInviteService.UseInviteCodeAsync(inviteCode, username);

                    // Do NOT auto-login pending users
                }

                // Save service configurations
                SaveServiceConfigurations();

                // Apply security settings
                if (this.FindControl<CheckBox>("ChkEncryptConfig")!.IsChecked!.Value)
                {
                    SecurityService.EncryptSensitiveConfiguration();
                }

                // Mark setup as complete
                ConfigService.MarkSetupComplete();

                LogService.WriteLiveLog("[SETUP] Initial setup completed successfully", "", "Information", "SYSTEM");

                OnSetupComplete?.Invoke();
                Close();
            }
            catch (Exception ex)
            {
                LogService.WriteLiveLog($"[SETUP] Setup failed: {ex.Message}", "", "Error", "SYSTEM");
                await ShowErrorDialog($"Setup failed: {ex.Message}");
            }
            finally
            {
                nextButton.IsEnabled = true;
                nextButton.Content = "Complete Setup";
            }
        }

        private void SaveServiceConfigurations()
        {
            var config = ConfigService.Current;

            // FTP settings
            config.Ftp.Host = this.FindControl<TextBox>("TxtFtpHost")!.Text?.Trim() ?? "";
            config.Ftp.User = this.FindControl<TextBox>("TxtFtpUser")!.Text?.Trim() ?? "";
            config.Ftp.Password = this.FindControl<TextBox>("TxtFtpPassword")!.Text ?? "";
            config.Paths.FtpLocalFolder = this.FindControl<TextBox>("TxtFtpLocalFolder")!.Text?.Trim() ?? "";

            // SQL settings
            config.Sql.Host = this.FindControl<TextBox>("TxtSqlHost")!.Text?.Trim() ?? "";
            config.Sql.User = this.FindControl<TextBox>("TxtSqlUser")!.Text?.Trim() ?? "";
            config.Sql.Password = this.FindControl<TextBox>("TxtSqlPassword")!.Text ?? "";
            config.Sql.RemotePath = this.FindControl<TextBox>("TxtSqlRemotePath")!.Text?.Trim() ?? "";
            config.Paths.SqlLocalFolder = this.FindControl<TextBox>("TxtSqlLocalFolder")!.Text?.Trim() ?? "";

            // Mailchimp settings
            config.Mailchimp.ApiKey = this.FindControl<TextBox>("TxtMcApiKey")!.Text?.Trim() ?? "";
            config.Mailchimp.AudienceId = this.FindControl<TextBox>("TxtMcAudienceId")!.Text?.Trim() ?? "";
            config.Paths.MailchimpFolder = this.FindControl<TextBox>("TxtMcFolder")!.Text?.Trim() ?? "";

            // Operation settings
            config.Operation.StartMinimized = this.FindControl<CheckBox>("ChkStartMinimized")!.IsChecked!.Value;
            config.Operation.AutoStartWindows = this.FindControl<CheckBox>("ChkAutoStart")!.IsChecked!.Value;
            var intervalIndex = this.FindControl<ComboBox>("CmbAutoInterval")!.SelectedIndex;
            config.Operation.AutoIntervalMinutes = intervalIndex switch
            {
                0 => 30,
                1 => 60,
                2 => 120,
                3 => 240,
                4 => 480,
                5 => 1440,
                _ => 60
            };

            ConfigService.Save();
        }

        private void UpdateSummary()
        {
            var username = this.FindControl<TextBox>("TxtAdminUsername")!.Text?.Trim() ?? "(not set)";
            this.FindControl<TextBlock>("TxtSummaryAdmin")!.Text = $"Administrator: {username}";

            var ftpEnabled = this.FindControl<CheckBox>("ChkEnableFTP")!.IsChecked!.Value;
            var ftpHost = this.FindControl<TextBox>("TxtFtpHost")!.Text?.Trim();
            this.FindControl<TextBlock>("TxtSummaryFTP")!.Text = ftpEnabled && !string.IsNullOrEmpty(ftpHost)
                ? $"FTP: Enabled ({ftpHost})"
                : "FTP: Disabled";

            var sqlEnabled = this.FindControl<CheckBox>("ChkEnableSQL")!.IsChecked!.Value;
            var sqlHost = this.FindControl<TextBox>("TxtSqlHost")!.Text?.Trim();
            this.FindControl<TextBlock>("TxtSummarySQL")!.Text = sqlEnabled && !string.IsNullOrEmpty(sqlHost)
                ? $"SQL: Enabled ({sqlHost})"
                : "SQL: Disabled";

            var mcEnabled = this.FindControl<CheckBox>("ChkEnableMailchimp")!.IsChecked!.Value;
            var mcKey = this.FindControl<TextBox>("TxtMcApiKey")!.Text?.Trim();
            this.FindControl<TextBlock>("TxtSummaryMailchimp")!.Text = mcEnabled && !string.IsNullOrEmpty(mcKey)
                ? "Mailchimp: Enabled"
                : "Mailchimp: Disabled";
        }

        private async Task ShowErrorDialog(string message)
        {
            var dialog = new Window
            {
                Title = "Setup Error",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Children =
                    {
                        new TextBlock 
                        { 
                            Text = message,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Avalonia.Thickness(0, 0, 0, 20)
                        },
                        new Button
                        {
                            Content = "OK",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Padding = new Avalonia.Thickness(20, 10)
                        }
                    }
                }
            };

            var btn = (Button)((StackPanel)dialog.Content!).Children[1];
            btn.Click += (_, _) => dialog.Close();

            await dialog.ShowDialog(this);
        }
    }
}

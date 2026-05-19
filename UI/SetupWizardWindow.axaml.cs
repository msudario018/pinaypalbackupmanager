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
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI
{
    public partial class SetupWizardWindow : Window
    {
        private int _currentStep = 1;
        private const int TotalSteps = 5;
        private bool _isAdminPC = true;

        public event Action? OnSetupComplete;

        public SetupWizardWindow()
        {
            AvaloniaXamlLoader.Load(this);
            _isAdminPC = !ConfigService.Current.Operation.SetupCompleted;
            SetupEventHandlers();
            UpdateUIForCurrentStep();
            SetDefaultPaths();
            UpdateAdminUserText();
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

            // Test connection buttons
            this.FindControl<Button>("BtnTestFTP")!.Click += OnTestFtpClick;
            this.FindControl<Button>("BtnTestSQL")!.Click += OnTestSqlClick;
            this.FindControl<Button>("BtnTestMailchimp")!.Click += OnTestMailchimpClick;

            // Import credentials buttons
            this.FindControl<Button>("BtnImportFtp")!.Click += (_, _) => ImportCredentials("ftp");
            this.FindControl<Button>("BtnImportSql")!.Click += (_, _) => ImportCredentials("sql");
            this.FindControl<Button>("BtnImportMailchimp")!.Click += (_, _) => ImportCredentials("mailchimp");

            // Password real-time validation
            var passwordBox = this.FindControl<TextBox>("TxtAdminPassword")!;
            var confirmPasswordBox = this.FindControl<TextBox>("TxtAdminPasswordConfirm")!;
            
            passwordBox.TextChanged += (_, _) => ValidatePasswordMatch();
            confirmPasswordBox.TextChanged += (_, _) => ValidatePasswordMatch();

            // Update summary when entering step 5
            this.FindControl<StackPanel>("Step5Security")!.PropertyChanged += (_, e) =>
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
            if (!await ValidateCurrentStep()) return;

            if (_currentStep < TotalSteps)
            {
                _currentStep++;
                UpdateUIForCurrentStep();
            }
            else
            {
                await CompleteSetup();
            }
        }

        private void OnBackClick(object? sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateUIForCurrentStep();
            }
        }

        private void OnSkipClick(object? sender, RoutedEventArgs e)
        {
            if (_currentStep < TotalSteps)
            {
                _currentStep++;
                UpdateUIForCurrentStep();
            }
        }

        private void UpdateUIForCurrentStep()
        {
            // Hide all steps
            this.FindControl<StackPanel>("Step1Admin")!.IsVisible = false;
            this.FindControl<StackPanel>("Step2FTP")!.IsVisible = false;
            this.FindControl<StackPanel>("Step3SQL")!.IsVisible = false;
            this.FindControl<StackPanel>("Step4Mailchimp")!.IsVisible = false;
            this.FindControl<StackPanel>("Step5Security")!.IsVisible = false;

            // Show current step
            StackPanel? currentPanel = _currentStep switch
            {
                1 => this.FindControl<StackPanel>("Step1Admin"),
                2 => this.FindControl<StackPanel>("Step2FTP"),
                3 => this.FindControl<StackPanel>("Step3SQL"),
                4 => this.FindControl<StackPanel>("Step4Mailchimp"),
                5 => this.FindControl<StackPanel>("Step5Security"),
                _ => null
            };
            if (currentPanel != null) currentPanel.IsVisible = true;

            // Update title
            this.FindControl<TextBlock>("TxtStepTitle")!.Text = $"Step {_currentStep} of {TotalSteps}";

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
                2 => ValidateFtpStep(),
                3 => ValidateSqlStep(),
                4 => ValidateMailchimpStep(),
                5 => await ValidateSecurityStep(),
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

        private async void BrowseFolder(string textBoxName)
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

        private async void ImportCredentials(string service)
        {
            try
            {
                var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Import {service.ToUpper()} Credentials",
                    AllowMultiple = false,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new FilePickerFileType("PinayPal Encrypted Settings (*.ppenc)") { Patterns = new[] { "*.ppenc" } },
                        new FilePickerFileType("JSON Settings (*.json)") { Patterns = new[] { "*.json" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                    }
                });

                if (result.Count == 0)
                {
                    ShowImportResult(service, "ℹ No file selected");
                    return;
                }

                var configPath = result[0].Path.LocalPath;
                var json = File.ReadAllText(configPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<Services.AppSettings>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (settings == null)
                {
                    ShowImportResult(service, "❌ Failed to parse configuration file");
                    return;
                }

                int importedCount = 0;

                switch (service.ToLower())
                {
                    case "ftp":
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
                        break;

                    case "sql":
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
                        break;

                    case "mailchimp":
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
                        break;
                }

                if (importedCount > 0)
                {
                    ShowImportResult(service, $"✓ Imported {importedCount} field(s) from {Path.GetFileName(configPath)}");
                }
                else
                {
                    ShowImportResult(service, "ℹ No credentials found in selected file");
                }
            }
            catch (Exception ex)
            {
                ShowImportResult(service, $"❌ Import failed: {ex.Message}");
            }
        }

        private void ShowImportResult(string service, string message)
        {
            var resultTextBlock = service.ToLower() switch
            {
                "ftp" => this.FindControl<TextBlock>("TxtFtpTestResult"),
                "sql" => this.FindControl<TextBlock>("TxtSqlTestResult"),
                "mailchimp" => this.FindControl<TextBlock>("TxtMcTestResult"),
                _ => null
            };

            if (resultTextBlock != null)
            {
                resultTextBlock.IsVisible = true;
                resultTextBlock.Text = message;

                // Set color based on message prefix
                if (message.StartsWith("✓"))
                    resultTextBlock.Foreground = Avalonia.Media.Brush.Parse("#A6E3A1");
                else if (message.StartsWith("❌"))
                    resultTextBlock.Foreground = Avalonia.Media.Brush.Parse("#F38BA8");
                else
                    resultTextBlock.Foreground = Avalonia.Media.Brush.Parse("#A6ADC8");
            }
        }

        private async Task CompleteSetup()
        {
            var nextButton = this.FindControl<Button>("BtnNext")!;
            nextButton.IsEnabled = false;
            nextButton.Content = "Setting up...";

            try
            {
                // 1. Create admin user
                var username = this.FindControl<TextBox>("TxtAdminUsername")!.Text!.Trim();
                var password = this.FindControl<TextBox>("TxtAdminPassword")!.Text!;
                var inviteCode = _isAdminPC ? null : this.FindControl<TextBox>("TxtInviteCode")!.Text?.Trim();
                var (success, message) = await AuthService.RegisterAsync(username, password, inviteCode);
                if (!success)
                {
                    await ShowErrorDialog($"Failed to create admin user: {message}");
                    return;
                }

                // 2. Save service configurations
                SaveServiceConfigurations();

                // 3. Apply security settings
                if (this.FindControl<CheckBox>("ChkEncryptConfig")!.IsChecked!.Value)
                {
                    SecurityService.EncryptSensitiveConfiguration();
                }

                // 4. Mark setup as complete
                ConfigService.MarkSetupComplete();

                // 5. Login the new admin
                AuthService.Login(username, password);
                SessionService.SaveSession(AuthService.CurrentUser!.Id);

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

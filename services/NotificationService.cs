using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Dto;
using PinayPalBackupManager.UI.UserControls;
using Avalonia;

namespace PinayPalBackupManager.Services
{
    public static class NotificationService
    {
        public static event Action<string, string, string>? OnToast;
        public static event Action<NotificationChannel, string, string>? OnExternalNotification;
        
        // Track currently open dialogs to prevent multiple popups
        private static readonly HashSet<string> _openDialogs = new();
        private static readonly object _dialogLock = new();
        
        // Track active toasts to prevent duplicates
        private static readonly List<Border> _activeToasts = new();
        private static readonly object _toastLock = new();
        
        // Notification enable/disable control
        private static bool _notificationsEnabled = false;
        private static readonly object _enableLock = new();
        
        // External notification settings
        private static NotificationSettings _settings = new();
        private static readonly object _settingsLock = new();
        private static readonly Queue<NotificationMessage> _notificationQueue = new();
        private static System.Timers.Timer? _queueProcessor;
        
        public static void EnableNotifications()
        {
            lock (_enableLock)
            {
                _notificationsEnabled = true;
            }
        }
        
        public static void DisableNotifications()
        {
            lock (_enableLock)
            {
                _notificationsEnabled = false;
            }
        }
        
        public static bool AreNotificationsEnabled()
        {
            lock (_enableLock)
            {
                return _notificationsEnabled;
            }
        }

        public static void ShowBackupToast(string title, string message, string type = "Info")
        {
            // Log the notification
            LogService.WriteLiveLog($"[NOTIFICATION] {title}: {message}", "", type, "SYSTEM");
            NotificationHistoryService.Add(title, message, type);
            OnToast?.Invoke(title, message, type);
            
            // Post smart popup bubble on AI floating widget for key operational events
            if (type.Equals("Success", StringComparison.OrdinalIgnoreCase) || type.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                AIAssistantService.PostEventBubble(title, message, type.Equals("Error", StringComparison.OrdinalIgnoreCase));
            }
            
            // Show visual toast with tea-green color palette only if notifications are enabled
            if (AreNotificationsEnabled())
            {
                _ = ShowVisualToastAsync(title, message, type);
            }
        }
        
        private static async Task ShowVisualToastAsync(string title, string message, string type)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Clear any existing toasts to prevent duplicates
                lock (_toastLock)
                {
                    foreach (var activeToast in _activeToasts.ToList())
                    {
                        if (activeToast.Parent is Grid parentGrid)
                        {
                            parentGrid.Children.Remove(activeToast);
                        }
                    }
                    _activeToasts.Clear();
                }
                
                var toast = new ToastNotification();
                toast.SetContent(title, message, type);
                
                // Create a container for the toast
                var container = new Border
                {
                    Child = toast,
                    IsHitTestVisible = true,
                    ZIndex = 9999,
                    Opacity = 0.9
                };
                
                // Track this toast
                lock (_toastLock)
                {
                    _activeToasts.Add(container);
                }
                
                // Find the main window
                var mainWindow = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;
                    
                if (mainWindow != null)
                {
                    // Add toast as an overlay to the main window (doesn't affect layout)
                    var mainGrid = mainWindow.Content as Grid;
                    if (mainGrid != null)
                    {
                        // Position toast in bottom-right corner — standard, less intrusive placement
                        container.Margin = new Thickness(0, 0, 20, 20);
                        container.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                        container.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                        
                        // Set toast to span all rows and be in the main content column
                        Grid.SetRow(container, 0); // Top row
                        Grid.SetRowSpan(container, 2); // Span both rows
                        Grid.SetColumn(container, 1); // Main content column
                        
                        // Add to main grid as overlay (doesn't affect layout)
                        mainGrid.Children.Add(container);
                        
                        // Auto-remove after 3 seconds
                        Task.Delay(3000).ContinueWith(_ =>
                        {
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (mainGrid.Children.Contains(container))
                                {
                                    mainGrid.Children.Remove(container);
                                }
                                
                                // Remove from active toasts list
                                lock (_toastLock)
                                {
                                    _activeToasts.Remove(container);
                                }
                            });
                        });
                    }
                }
            });
        }

        public static async Task ShowMessageBoxAsync(string message, string title, ButtonEnum buttons = ButtonEnum.Ok, Icon icon = Icon.Info)
        {
            var dialogKey = $"msgbox_{title}";
            
            lock (_dialogLock)
            {
                if (_openDialogs.Contains(dialogKey))
                    return;
                _openDialogs.Add(dialogKey);
            }
            
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = title,
                        ContentMessage = message,
                        ButtonDefinitions = buttons,
                        Icon = icon,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Topmost = true,
                        CanResize = false,
                        SystemDecorations = SystemDecorations.BorderOnly
                    });
                    await box.ShowAsync();
                });
            }
            finally
            {
                lock (_dialogLock)
                {
                    _openDialogs.Remove(dialogKey);
                }
            }
        }

        public static async Task<bool> ConfirmAsync(string message, string title, Icon icon = Icon.Question)
        {
            var dialogKey = $"confirm_{title}";
            
            lock (_dialogLock)
            {
                if (_openDialogs.Contains(dialogKey))
                    return false;
                _openDialogs.Add(dialogKey);
            }
            
            try
            {
                // Use custom confirmation dialog with tea-green color palette
                return await ConfirmDialog.ShowAsync(title, message);
            }
            finally
            {
                lock (_dialogLock)
                {
                    _openDialogs.Remove(dialogKey);
                }
            }
        }
        
        // Helper to check if any dialog is open (for custom dialogs)
        public static bool IsDialogOpen(string dialogKey)
        {
            lock (_dialogLock)
            {
                return _openDialogs.Contains(dialogKey);
            }
        }
        
        // Helper to register custom dialogs
        public static void RegisterDialog(string dialogKey)
        {
            lock (_dialogLock)
            {
                _openDialogs.Add(dialogKey);
            }
        }
        
        // Helper to unregister custom dialogs
        public static void UnregisterDialog(string dialogKey)
        {
            lock (_dialogLock)
            {
                _openDialogs.Remove(dialogKey);
            }
        }
        
        // External notification methods
        public static void ConfigureNotifications(NotificationSettings settings)
        {
            lock (_settingsLock)
            {
                _settings = settings;
                
                // Start queue processor if not already running
                if (_queueProcessor == null && (settings.EmailEnabled || settings.SmsEnabled))
                {
                    _queueProcessor = new System.Timers.Timer(5000); // Process every 5 seconds
                    _queueProcessor.Elapsed += ProcessNotificationQueue;
                    _queueProcessor.Start();
                    
                    LogService.WriteSystemLog("[NOTIFICATION] External notification service started", "Information", "SYSTEM");
                }
                else if (_queueProcessor != null && !settings.EmailEnabled && !settings.SmsEnabled)
                {
                    _queueProcessor.Stop();
                    _queueProcessor.Dispose();
                    _queueProcessor = null;
                    
                    LogService.WriteSystemLog("[NOTIFICATION] External notification service stopped", "Information", "SYSTEM");
                }
            }
        }
        
        public static void SendExternalNotification(NotificationChannel channel, string subject, string message, NotificationPriority priority = NotificationPriority.Normal)
        {
            if (!IsChannelEnabled(channel)) return;
            
            var notification = new NotificationMessage
            {
                Id = Guid.NewGuid().ToString(),
                Channel = channel,
                Subject = subject,
                Message = message,
                Priority = priority,
                CreatedAt = DateTime.UtcNow,
                RetryCount = 0,
                MaxRetries = 3
            };
            
            lock (_settingsLock)
            {
                _notificationQueue.Enqueue(notification);
            }
            
            // Also trigger event for UI components
            OnExternalNotification?.Invoke(channel, subject, message);
            
            LogService.WriteSystemLog($"[NOTIFICATION] Queued {channel} notification: {subject}", "Information", "SYSTEM");
        }
        
        public static void SendAlert(string title, string message, AlertSeverity severity = AlertSeverity.Warning)
        {
            var priority = severity switch
            {
                AlertSeverity.Critical => NotificationPriority.High,
                AlertSeverity.Warning => NotificationPriority.Medium,
                _ => NotificationPriority.Low
            };
            
            // Send toast notification
            ShowBackupToast("Alert", title, severity.ToString());
            
            // Send external notifications
            if (_settings.EmailEnabled)
            {
                SendExternalNotification(NotificationChannel.Email, $"[{severity}] {title}", message, priority);
            }
            
            if (_settings.SmsEnabled)
            {
                SendExternalNotification(NotificationChannel.SMS, $"[{severity}] {title}", message, priority);
            }
        }
        
        private static async void ProcessNotificationQueue(object? sender, System.Timers.ElapsedEventArgs e)
        {
            List<NotificationMessage> toProcess;
            
            lock (_settingsLock)
            {
                toProcess = _notificationQueue.ToList();
                _notificationQueue.Clear();
            }
            
            foreach (var notification in toProcess)
            {
                try
                {
                    bool success = notification.Channel switch
                    {
                        NotificationChannel.Email => await SendEmailNotification(notification),
                        NotificationChannel.SMS => await SendSmsNotification(notification),
                        _ => false
                    };
                    
                    if (!success && notification.RetryCount < notification.MaxRetries)
                    {
                        notification.RetryCount++;
                        lock (_settingsLock)
                        {
                            _notificationQueue.Enqueue(notification);
                        }
                        
                        LogService.WriteSystemLog($"[NOTIFICATION] Retrying {notification.Channel} notification (attempt {notification.RetryCount}/{notification.MaxRetries})", "Warning", "SYSTEM");
                    }
                    else if (success)
                    {
                        LogService.WriteSystemLog($"[NOTIFICATION] {notification.Channel} notification sent successfully: {notification.Subject}", "Information", "SYSTEM");
                    }
                    else
                    {
                        LogService.WriteSystemLog($"[NOTIFICATION] Failed to send {notification.Channel} notification after {notification.MaxRetries} retries: {notification.Subject}", "Error", "SYSTEM");
                    }
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[NOTIFICATION] Error processing notification: {ex.Message}", "Error", "SYSTEM");
                }
            }
        }
        
        private static async Task<bool> SendEmailNotification(NotificationMessage notification)
        {
            try
            {
                using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    EnableSsl = _settings.SmtpUseSsl,
                    Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword)
                };
                
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_settings.EmailFrom),
                    Subject = notification.Subject,
                    Body = $"{notification.Message}\n\nSent at: {notification.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}\nPriority: {notification.Priority}",
                    IsBodyHtml = false
                };
                
                foreach (var recipient in _settings.EmailRecipients)
                {
                    mailMessage.To.Add(recipient);
                }
                
                await client.SendMailAsync(mailMessage);
                return true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NOTIFICATION] Email send failed: {ex.Message}", "Error", "SYSTEM");
                return false;
            }
        }

        public static async Task<bool> SendDirectEmailAsync(string toEmail, string subject, string body, bool isHtml = false)
        {
            if (string.IsNullOrWhiteSpace(_settings.SmtpHost) || string.IsNullOrWhiteSpace(_settings.EmailFrom))
            {
                return false;
            }

            try
            {
                using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    EnableSsl = _settings.SmtpUseSsl,
                    Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword)
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_settings.EmailFrom, "PinayPal Backup Manager"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = isHtml
                };
                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);
                return true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[Notification] Failed to send email to {toEmail}: {ex.Message}", "Error", "SYSTEM");
                return false;
            }
        }

        public static async Task<(bool success, string message)> SendTestEmailAsync(string? targetEmail = null)
        {
            var recipient = !string.IsNullOrWhiteSpace(targetEmail) ? targetEmail : _settings.EmailRecipients.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
                return (false, "SMTP Host is not configured.");
            if (string.IsNullOrWhiteSpace(_settings.EmailFrom))
                return (false, "Sender email (EmailFrom) is not configured.");
            if (string.IsNullOrWhiteSpace(recipient))
                return (false, "Target recipient email is required.");

            try
            {
                using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                {
                    EnableSsl = _settings.SmtpUseSsl,
                    Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword),
                    Timeout = 12000
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_settings.EmailFrom, "PinayPal Backup Manager"),
                    Subject = "✅ PinayPal Backup Manager - Verified Email Alerting",
                    Body = EmailTemplateService.BuildTestEmail(_settings.SmtpHost, _settings.SmtpPort, _settings.EmailFrom),
                    IsBodyHtml = true
                };
                mailMessage.To.Add(recipient!);
                await client.SendMailAsync(mailMessage);
                return (true, "Test email delivered successfully!");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static void SendBackupTelegramAlert(string serviceName, string status, string details)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TelegramService.SendBackupAlertAsync(serviceName, status, details);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[TELEGRAM] Failed to send backup alert: {ex.Message}", "Error", "SYSTEM");
                }
            });
        }

        public static void SendDisconnectTelegramAlert(string reason)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TelegramService.SendDisconnectAlertAsync(reason);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[TELEGRAM] Failed to send disconnect alert: {ex.Message}", "Error", "SYSTEM");
                }
            });
        }

        public static void SendOutdatedTelegramAlert(string serviceName, string detail)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TelegramService.SendOutdatedAlertAsync(serviceName, detail);
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[TELEGRAM] Failed to send outdated alert: {ex.Message}", "Error", "SYSTEM");
                }
            });
        }

        public static void SendBackupEmailAlert(string serviceName, bool success, string details)
        {
            SendBackupTelegramAlert(serviceName, success ? "Completed" : "Failed", details);

            if (!_settings.EmailEnabled) return;
            if (success && !_settings.NotifyOnBackupSuccess) return;
            if (!success && !_settings.NotifyOnBackupFailure) return;

            var recipients = _settings.EmailRecipients.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            if (recipients.Count == 0) return;

            _ = Task.Run(async () =>
            {
                var icon = success ? "✅" : "🚨";
                var statusText = success ? "COMPLETED" : "FAILED";
                var subject = $"{icon} PinayPal Backup [{serviceName.ToUpper()}]: {statusText}";
                var htmlBody = EmailTemplateService.BuildBackupAlertEmail(serviceName, success, details);

                foreach (var recipient in recipients)
                {
                    await SendDirectEmailAsync(recipient, subject, htmlBody, isHtml: true);
                }
            });
        }

        public static void SendDisconnectAlertEmail(string reason)
        {
            SendDisconnectTelegramAlert(reason);

            if (!_settings.EmailEnabled || !_settings.NotifyOnDisconnect) return;
            var recipients = _settings.EmailRecipients.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            if (recipients.Count == 0) return;

            _ = Task.Run(async () =>
            {
                var subject = "⚠️ PinayPal Alert: Local / Cloudflare Disconnected";
                var htmlBody = EmailTemplateService.BuildDisconnectAlertEmail(reason);

                foreach (var recipient in recipients)
                {
                    await SendDirectEmailAsync(recipient, subject, htmlBody, isHtml: true);
                }
            });
        }

        public static void SendOutdatedAlertEmail(string serviceName, string detail)
        {
            SendOutdatedTelegramAlert(serviceName, detail);

            if (!_settings.EmailEnabled || !_settings.NotifyOnOutdated) return;
            var recipients = _settings.EmailRecipients.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            if (recipients.Count == 0) return;

            _ = Task.Run(async () =>
            {
                var subject = $"⚠️ PinayPal Backup Outdated: {serviceName.ToUpper()}";
                var htmlBody = EmailTemplateService.BuildOutdatedAlertEmail(serviceName, detail);

                foreach (var recipient in recipients)
                {
                    await SendDirectEmailAsync(recipient, subject, htmlBody, isHtml: true);
                }
            });
        }
        
        private static async Task<bool> SendSmsNotification(NotificationMessage notification)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.SmsApiKey))
                {
                    LogService.WriteSystemLog("[NOTIFICATION] SMS API key not configured", "Warning", "SYSTEM");
                    return false;
                }

                if (_settings.SmsRecipients.Count == 0)
                {
                    LogService.WriteSystemLog("[NOTIFICATION] No SMS recipients configured", "Warning", "SYSTEM");
                    return false;
                }

                // Implement Twilio SMS API
                if (_settings.SmsProvider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
                {
                    return await SendTwilioSms(notification);
                }
                // Add other SMS providers here (AWS SNS, etc.)
                else
                {
                    LogService.WriteSystemLog($"[NOTIFICATION] Unsupported SMS provider: {_settings.SmsProvider}", "Error", "SYSTEM");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NOTIFICATION] SMS send failed: {ex.Message}", "Error", "SYSTEM");
                return false;
            }
        }

        private static async Task<bool> SendTwilioSms(NotificationMessage notification)
        {
            try
            {
                // Parse Twilio credentials from API key (format: "AccountSID:AuthToken:FromNumber")
                var parts = _settings.SmsApiKey.Split(':');
                if (parts.Length != 3)
                {
                    LogService.WriteSystemLog("[NOTIFICATION] Invalid Twilio API key format. Expected: AccountSID:AuthToken:FromNumber", "Error", "SYSTEM");
                    return false;
                }

                var accountSid = parts[0];
                var authToken = parts[1];
                var fromNumber = parts[2];

                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", 
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{accountSid}:{authToken}")));

                var successCount = 0;
                var failureCount = 0;

                foreach (var recipient in _settings.SmsRecipients)
                {
                    try
                    {
                        var content = new System.Net.Http.FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("From", fromNumber),
                            new KeyValuePair<string, string>("To", recipient),
                            new KeyValuePair<string, string>("Body", $"{notification.Subject}: {notification.Message}")
                        });

                        var response = await httpClient.PostAsync(
                            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json", 
                            content);

                        if (response.IsSuccessStatusCode)
                        {
                            successCount++;
                            LogService.WriteSystemLog($"[NOTIFICATION] SMS sent successfully to {recipient}", "Information", "SYSTEM");
                        }
                        else
                        {
                            failureCount++;
                            var errorContent = await response.Content.ReadAsStringAsync();
                            LogService.WriteSystemLog($"[NOTIFICATION] SMS failed to {recipient}: {errorContent}", "Error", "SYSTEM");
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        LogService.WriteSystemLog($"[NOTIFICATION] SMS error to {recipient}: {ex.Message}", "Error", "SYSTEM");
                    }
                }

                LogService.WriteSystemLog($"[NOTIFICATION] SMS batch completed: {successCount} success, {failureCount} failures", "Information", "SYSTEM");
                return successCount > 0 && failureCount == 0;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NOTIFICATION] Twilio SMS error: {ex.Message}", "Error", "SYSTEM");
                return false;
            }
        }
        
        private static bool IsChannelEnabled(NotificationChannel channel)
        {
            lock (_settingsLock)
            {
                return channel switch
                {
                    NotificationChannel.Email => _settings.EmailEnabled,
                    NotificationChannel.SMS => _settings.SmsEnabled,
                    _ => false
                };
            }
        }
        
        private static bool _settingsLoaded = false;
        
        public static NotificationSettings GetSettings()
        {
            lock (_settingsLock)
            {
                if (!_settingsLoaded)
                {
                    LoadSettingsInternal();
                }
                return _settings;
            }
        }
        
        public static NotificationSettings Settings
        {
            get => GetSettings();
            set
            {
                lock (_settingsLock)
                {
                    _settings = value;
                    _settingsLoaded = true;
                }
            }
        }

        public static void SaveSettings()
        {
            lock (_settingsLock)
            {
                try
                {
                    var settingsPath = AppDataPaths.GetDataPath("notifications.json");
                    var dir = System.IO.Path.GetDirectoryName(settingsPath);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }

                    if (string.IsNullOrWhiteSpace(_settings.EmailFrom) && !string.IsNullOrWhiteSpace(_settings.SmtpUsername))
                    {
                        _settings.EmailFrom = _settings.SmtpUsername;
                    }

                    var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(settingsPath, json);
                    _settingsLoaded = true;

                    // Immediately reconfigure active runtime dispatchers
                    ConfigureNotifications(_settings);
                    TelegramService.Restart();
                    
                    LogService.WriteSystemLog($"[NOTIFICATION] Settings saved to {settingsPath} (Telegram: {_settings.TelegramEnabled}, Recipient: {_settings.RecipientEmail})", "Information", "SYSTEM");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[NOTIFICATION] Failed to save settings: {ex.Message}", "Error", "SYSTEM");
                }
            }
        }
        
        public static void LoadSettings()
        {
            lock (_settingsLock)
            {
                LoadSettingsInternal();
            }
        }

        private static void LoadSettingsInternal()
        {
            try
            {
                var settingsPath = AppDataPaths.GetDataPath("notifications.json");
                if (!System.IO.File.Exists(settingsPath))
                {
                    var fallback = AppDataPaths.GetExistingOrCurrentPath("notifications.json");
                    if (System.IO.File.Exists(fallback))
                    {
                        settingsPath = fallback;
                    }
                }

                if (System.IO.File.Exists(settingsPath))
                {
                    var json = System.IO.File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<NotificationSettings>(json);
                    
                    if (settings != null)
                    {
                        _settings = settings;
                        if (string.IsNullOrWhiteSpace(_settings.EmailFrom) && !string.IsNullOrWhiteSpace(_settings.SmtpUsername))
                        {
                            _settings.EmailFrom = _settings.SmtpUsername;
                        }

                        // Reconfigure with loaded settings
                        ConfigureNotifications(settings);
                        TelegramService.Restart();
                        LogService.WriteSystemLog($"[NOTIFICATION] Loaded notification settings (Telegram: {_settings.TelegramEnabled}, Recipient: {_settings.RecipientEmail})", "Information", "SYSTEM");
                    }
                }
                _settingsLoaded = true;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NOTIFICATION] Failed to load settings: {ex.Message}", "Error", "SYSTEM");
            }
        }
    }
    
    public class NotificationSettings
    {
        // Telegram Bot Alerting & Remote Control
        public bool TelegramEnabled { get; set; } = false;
        public string TelegramBotToken { get; set; } = string.Empty;
        public string TelegramChatId { get; set; } = string.Empty;
        public bool NotifyOnBackupStart { get; set; } = true;
        public bool NotifyOnBackupSuccess { get; set; } = true;
        public bool NotifyOnBackupFailure { get; set; } = true;
        public bool NotifyOnDisconnect { get; set; } = true;
        public bool NotifyOnOutdated { get; set; } = true;

        public bool EmailEnabled { get; set; } = false;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool EmailAlertsEnabled { get => EmailEnabled; set => EmailEnabled = value; }

        public bool SmsEnabled { get; set; } = false;
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool SmtpSsl { get => SmtpUseSsl; set => SmtpUseSsl = value; }

        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public string EmailFrom { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore]
        public string SenderEmail { get => EmailFrom; set => EmailFrom = value; }

        public List<string> EmailRecipients { get; set; } = new();

        public string RecipientEmail
        {
            get => EmailRecipients.FirstOrDefault() ?? string.Empty;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var val = value.Trim();
                    EmailRecipients.Clear();
                    EmailRecipients.Add(val);
                }
                else
                {
                    EmailRecipients.Clear();
                }
            }
        }
        public List<string> SmsRecipients { get; set; } = new();
        public string SmsApiKey { get; set; } = string.Empty;
        public string SmsProvider { get; set; } = "Twilio"; // Twilio, AWS SNS, etc.
    }
    
    public class NotificationMessage
    {
        public string Id { get; set; } = string.Empty;
        public NotificationChannel Channel { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationPriority Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
    }
    
    public enum NotificationChannel
    {
        Email,
        SMS,
        Push,
        Webhook
    }
    
    public enum NotificationPriority
    {
        Low,
        Normal,
        Medium,
        High,
        Critical
    }
}

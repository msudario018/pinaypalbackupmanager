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
        
        public static NotificationSettings GetSettings()
        {
            lock (_settingsLock)
            {
                return _settings;
            }
        }
        
        public static void SaveSettings()
        {
            try
            {
                var settingsPath = System.IO.Path.Combine(AppDataPaths.CurrentDirectory, "notifications.json");
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(settingsPath, json);
                
                LogService.WriteSystemLog("[NOTIFICATION] Settings saved", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NOTIFICATION] Failed to save settings: {ex.Message}", "Error", "SYSTEM");
            }
        }
        
        public static void LoadSettings()
        {
            try
            {
                var settingsPath = System.IO.Path.Combine(AppDataPaths.CurrentDirectory, "notifications.json");
                if (System.IO.File.Exists(settingsPath))
                {
                    var json = System.IO.File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<NotificationSettings>(json);
                    
                    if (settings != null)
                    {
                        lock (_settingsLock)
                        {
                            _settings = settings;
                        }
                        
                        // Reconfigure with loaded settings
                        ConfigureNotifications(settings);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[NOTIFICATION] Failed to load settings: {ex.Message}", "Error", "SYSTEM");
            }
        }
    }
    
    public class NotificationSettings
    {
        public bool EmailEnabled { get; set; } = false;
        public bool SmsEnabled { get; set; } = false;
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;
        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public string EmailFrom { get; set; } = string.Empty;
        public List<string> EmailRecipients { get; set; } = new();
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

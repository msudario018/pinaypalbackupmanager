using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        
        // Track currently open dialogs to prevent multiple popups
        private static readonly HashSet<string> _openDialogs = new();
        private static readonly object _dialogLock = new();
        
        // Track active toasts to prevent duplicates
        private static readonly List<Border> _activeToasts = new();
        private static readonly object _toastLock = new();
        
        // Notification enable/disable control
        private static bool _notificationsEnabled = false;
        private static readonly object _enableLock = new();
        
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
                        // Position toast in top-right corner using absolute positioning
                        container.Margin = new Thickness(0, 80, 20, 0);
                        container.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                        container.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                        
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
                        Topmost = true
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
    }
}

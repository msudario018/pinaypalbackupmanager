using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Dto;

namespace PinayPalBackupManager.Services
{
    public static class NotificationService
    {
        public static event Action<string, string, string>? OnToast;
        
        // Track currently open dialogs to prevent multiple popups
        private static readonly HashSet<string> _openDialogs = new();
        private static readonly object _dialogLock = new();

        public static void ShowBackupToast(string title, string message, string type = "Info")
        {
            // Since Avalonia doesn't have a built-in native toast, we'll log it for now
            LogService.WriteLiveLog($"[NOTIFICATION] {title}: {message}", "", type, "SYSTEM");
            OnToast?.Invoke(title, message, type);
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
                return await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = title,
                        ContentMessage = message,
                        ButtonDefinitions = ButtonEnum.YesNo,
                        Icon = icon,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Topmost = true
                    });
                    var result = await box.ShowAsync();
                    return result == ButtonResult.Yes;
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

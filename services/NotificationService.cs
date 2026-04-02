using System;
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

        public static void ShowBackupToast(string title, string message, string type = "Info")
        {
            // Since Avalonia doesn't have a built-in native toast, we'll log it for now
            LogService.WriteLiveLog($"[NOTIFICATION] {title}: {message}", "", type, "SYSTEM");
            OnToast?.Invoke(title, message, type);
        }

        public static async Task ShowMessageBoxAsync(string message, string title, ButtonEnum buttons = ButtonEnum.Ok, Icon icon = Icon.Info)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var box = MessageBoxManager.GetMessageBoxStandard(title, message, buttons, icon);
                await box.ShowAsync();
            });
        }

        public static async Task<bool> ConfirmAsync(string message, string title, Icon icon = Icon.Question)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo, icon);
                var result = await box.ShowAsync();
                return result == ButtonResult.Yes;
            });
        }
    }
}

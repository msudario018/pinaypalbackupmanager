using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PinayPalBackupManager.Services
{
    public static class AppIconHelper
    {
        public const string AppUserModelId = "PinayPal.PinayPalBackupManager";

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const uint WM_SETICON = 0x0080;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const IntPtr ICON_SMALL = 0;
        private const IntPtr ICON_BIG = 1;
        private const int SW_RESTORE = 9;

        private static EventWaitHandle? _restoreSignal;
        private static Thread? _listenerThread;

        public static void EnsureAppUserModelId()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
                }
                catch { }
            }
        }

        public static bool CheckSingleInstanceAndSignalExisting()
        {
            if (!OperatingSystem.IsWindows()) return true;

            try
            {
                bool createdNew;
                _restoreSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "PinayPalBackupManager_RestoreSignal", out createdNew);

                if (!createdNew)
                {
                    // An instance is already running. Signal it to restore its window and exit this duplicate process.
                    try
                    {
                        _restoreSignal.Set();
                    }
                    catch { }
                    return false;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        public static void StartSingleInstanceListener(Action onRestoreRequested)
        {
            if (!OperatingSystem.IsWindows() || _restoreSignal == null) return;

            try
            {
                _listenerThread = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            _restoreSignal.WaitOne();
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                onRestoreRequested?.Invoke();
                            });
                        }
                        catch
                        {
                            break;
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "SingleInstanceListener"
                };
                _listenerThread.Start();
            }
            catch { }
        }

        public static WindowIcon? GetAppWindowIcon()
        {
            // 1. Try avares resource stream for logo.ico
            try
            {
                var uri = new Uri("avares://PinayPalBackupManager/Assets/logo.ico");
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    return new WindowIcon(stream);
                }
            }
            catch { }

            // 2. Try avares resource stream for logo.png
            try
            {
                var uriPng = new Uri("avares://PinayPalBackupManager/Assets/logo.png");
                if (AssetLoader.Exists(uriPng))
                {
                    using var stream = AssetLoader.Open(uriPng);
                    return new WindowIcon(new Bitmap(stream));
                }
            }
            catch { }

            // 3. Try physical file path logo.ico
            try
            {
                var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
                if (File.Exists(icoPath))
                {
                    return new WindowIcon(icoPath);
                }
            }
            catch { }

            // 4. Try physical file path logo.png
            try
            {
                var pngPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
                if (File.Exists(pngPath))
                {
                    return new WindowIcon(new Bitmap(pngPath));
                }
            }
            catch { }

            return null;
        }

        public static void SetNativeWindowIcon(Window window)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle == null || handle.Handle == IntPtr.Zero) return;

                var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
                if (!File.Exists(icoPath))
                {
                    icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.ico");
                }

                if (File.Exists(icoPath))
                {
                    // Big icon (32x32) for Taskbar & Alt+Tab
                    IntPtr hBigIcon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                    if (hBigIcon != IntPtr.Zero)
                    {
                        SendMessage(handle.Handle, WM_SETICON, ICON_BIG, hBigIcon);
                    }

                    // Small icon (16x16) for Title bar
                    IntPtr hSmallIcon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    if (hSmallIcon != IntPtr.Zero)
                    {
                        SendMessage(handle.Handle, WM_SETICON, ICON_SMALL, hSmallIcon);
                    }
                }
            }
            catch { }
        }

        public static void ForceForeground(Window window)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle != null && handle.Handle != IntPtr.Zero)
                {
                    ShowWindow(handle.Handle, SW_RESTORE);
                    SetForegroundWindow(handle.Handle);
                }
            }
            catch { }
        }
    }
}

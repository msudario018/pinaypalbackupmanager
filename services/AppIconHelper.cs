using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                var currentProc = Process.GetCurrentProcess();
                var candidateProcs = Process.GetProcessesByName(currentProc.ProcessName)
                    .Where(p => p.Id != currentProc.Id)
                    .ToList();

                // Filter candidates by session ID, exit status, and responsiveness
                var validOthers = new List<Process>();
                foreach (var other in candidateProcs)
                {
                    try
                    {
                        if (other.HasExited) continue;
                        if (other.SessionId != currentProc.SessionId) continue;

                        // If the process is in the middle of shutting down (e.g. update restart handoff),
                        // give it a brief moment to finish exiting.
                        if (other.WaitForExit(600))
                        {
                            continue; // Process exited cleanly
                        }

                        // If the process is hung and not responding, kill the zombie so it doesn't block the user
                        if (!other.Responding)
                        {
                            try { other.Kill(); } catch { }
                            continue;
                        }

                        validOthers.Add(other);
                    }
                    catch
                    {
                        // Process access denied or already dead
                    }
                }

                // If no other valid PinayPal process is running, this instance is primary!
                if (validOthers.Count == 0)
                {
                    _restoreSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "PinayPalBackupManager_RestoreSignal", out _);
                    return true;
                }

                // Try to signal existing running instance to restore its window
                bool signaled = false;
                try
                {
                    if (EventWaitHandle.TryOpenExisting("PinayPalBackupManager_RestoreSignal", out var signal))
                    {
                        signal.Set();
                        signal.Dispose();
                        signaled = true;
                    }
                }
                catch { }

                // Also try to bring existing process window to front directly via Win32 SetForegroundWindow
                bool restoredWindow = false;
                foreach (var other in validOthers)
                {
                    try
                    {
                        other.Refresh();
                        if (other.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(other.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(other.MainWindowHandle);
                            restoredWindow = true;
                        }
                    }
                    catch { }
                }

                // If neither the event wait handle could be signaled nor any window handle was found,
                // the existing process is an orphaned background zombie without any UI or listener.
                // Terminate orphaned ghosts so the user's launch action succeeds.
                if (!signaled && !restoredWindow)
                {
                    foreach (var other in validOthers)
                    {
                        try { other.Kill(); } catch { }
                    }
                    _restoreSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "PinayPalBackupManager_RestoreSignal", out _);
                    return true;
                }

                // An active, responsive instance was found and signaled to restore
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static Action? _onRestoreRequested;

        public static void StartSingleInstanceListener(Action onRestoreRequested)
        {
            if (!OperatingSystem.IsWindows()) return;

            _onRestoreRequested = onRestoreRequested;

            try
            {
                if (_restoreSignal == null)
                {
                    _restoreSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "PinayPalBackupManager_RestoreSignal");
                }

                if (_listenerThread != null && _listenerThread.IsAlive) return;

                _listenerThread = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            _restoreSignal.WaitOne();
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                _onRestoreRequested?.Invoke();
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        public static void BringToForeground(Window window) => ForceForeground(window);

        public static void ForceForeground(Window window)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle != null && handle.Handle != IntPtr.Zero)
                {
                    IntPtr hwnd = handle.Handle;
                    ShowWindow(hwnd, SW_RESTORE);

                    IntPtr fgHwnd = GetForegroundWindow();
                    uint fgThread = GetWindowThreadProcessId(fgHwnd, IntPtr.Zero);
                    uint curThread = (uint)Environment.CurrentManagedThreadId;

                    if (fgThread != 0 && fgThread != curThread)
                    {
                        AttachThreadInput(curThread, fgThread, true);
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                        AttachThreadInput(curThread, fgThread, false);
                    }
                    else
                    {
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                    }
                }
            }
            catch { }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static WndProcDelegate? _taskbarSubclassDelegate;
        private static IntPtr _originalWndProc = IntPtr.Zero;
        private static uint _wmTaskbarCreated = 0;

        /// <summary>
        /// Hooks the Win32 window message for "TaskbarCreated".
        /// When Windows Explorer restarts, or when resuming from sleep/screen lock,
        /// Windows broadcasts this message so applications can restore their system tray icons.
        /// </summary>
        public static void HookTaskbarCreated(Window window, Action onTaskbarCreated)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle == null || handle.Handle == IntPtr.Zero) return;

                if (_wmTaskbarCreated == 0)
                {
                    _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
                }

                IntPtr hwnd = handle.Handle;
                _taskbarSubclassDelegate = (hWnd, msg, wParam, lParam) =>
                {
                    if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try { onTaskbarCreated?.Invoke(); } catch { }
                        });
                    }
                    return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
                };

                IntPtr newProcPtr = Marshal.GetFunctionPointerForDelegate(_taskbarSubclassDelegate);
                _originalWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, newProcPtr);
            }
            catch { }
        }
    }
}

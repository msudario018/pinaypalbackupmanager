using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace PinayPalBackupManager.Services
{
    public static class WindowStateService
    {
        private static readonly string StateFile = Path.Combine(AppDataPaths.CurrentDirectory, "windowstate.json");

        private record WindowState(int X, int Y, int W, int H);

        public static void Restore(Window window)
        {
            try
            {
                if (!File.Exists(StateFile)) return;
                var state = JsonSerializer.Deserialize<WindowState>(File.ReadAllText(StateFile));
                if (state == null || state.W < 600 || state.H < 400) return;
                window.Width    = state.W;
                window.Height   = state.H;
                window.Position = new PixelPoint(Math.Max(0, state.X), Math.Max(0, state.Y));
            }
            catch { }
        }

        public static void Save(Window window)
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.CurrentDirectory);
                var state = new WindowState(window.Position.X, window.Position.Y, (int)window.Width, (int)window.Height);
                File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
            }
            catch { }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Helper to diagnose, register URL ACL reservations, and configure Windows Firewall
    /// for seamless local IP and LAN connections from iOS devices and other network computers.
    /// </summary>
    public static class NetworkAccessHelper
    {
        public const string FirewallRuleName = "PinayPal Backup Manager (Port 8080)";

        /// <summary>
        /// Attempts to configure URL reservation and Windows Firewall rule via an elevated UAC prompt.
        /// </summary>
        public static async Task<(bool Success, string Message)> ConfigureLanAccessAsync(int port = 8080)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Construct commands to register URL ACL and open Windows Firewall port
                    var commands = new[]
                    {
                        $"netsh http add urlacl url=http://+:{port}/ user=Everyone",
                        $"netsh advfirewall firewall delete rule name=\"PinayPal Backup Manager (Port {port})\" protocol=TCP localport={port}",
                        $"netsh advfirewall firewall add rule name=\"PinayPal Backup Manager (Port {port})\" dir=in action=allow protocol=TCP localport={port} profile=any description=\"Allow iOS app and LAN devices to connect to PinayPal Backup Manager Web Server\""
                    };

                    var scriptContent = string.Join("\r\n", commands);
                    var tempScriptPath = Path.Combine(Path.GetTempPath(), $"pinaypal_lan_setup_{Guid.NewGuid():N}.cmd");
                    File.WriteAllText(tempScriptPath, scriptContent);

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{tempScriptPath}\"",
                        Verb = "runas", // Triggers UAC elevation prompt
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return (false, "Could not start elevated configuration process.");
                    }

                    process.WaitForExit(15000);

                    try { File.Delete(tempScriptPath); } catch { }

                    if (process.ExitCode == 0)
                    {
                        LogService.WriteSystemLog($"[NetworkAccessHelper] Successfully configured URL reservation and Windows Firewall for port {port}.", "Information", "SYSTEM");
                        return (true, "LAN access and Windows Firewall rule configured successfully!");
                    }
                    else
                    {
                        LogService.WriteSystemLog($"[NetworkAccessHelper] Process exited with code {process.ExitCode}", "Warning", "SYSTEM");
                        return (true, "Setup completed with exit code " + process.ExitCode);
                    }
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED (User cancelled UAC)
                {
                    LogService.WriteSystemLog("[NetworkAccessHelper] User declined administrator elevation prompt.", "Warning", "SYSTEM");
                    return (false, "Administrator approval was cancelled. LAN access may remain restricted.");
                }
                catch (Exception ex)
                {
                    LogService.WriteSystemLog($"[NetworkAccessHelper] Failed to configure LAN access: {ex.Message}", "Error", "SYSTEM");
                    return (false, $"Configuration failed: {ex.Message}");
                }
            });
        }
    }
}

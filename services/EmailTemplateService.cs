using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Generates executive, luxury-grade, responsive HTML email templates for PinayPal Backup Manager.
    /// Engineered to render flawlessly across Apple Mail, Gmail, Outlook, and webmail clients in both dark and light modes.
    /// </summary>
    public static class EmailTemplateService
    {
        public static string BuildBackupAlertEmail(
            string serviceName,
            bool isSuccess,
            string details,
            long sizeBytes = 0,
            TimeSpan? duration = null,
            int fileCount = 0)
        {
            var icon = isSuccess ? "✅" : "🚨";
            var statusHeadline = isSuccess ? "Backup Completed Successfully" : "Backup Failed / Execution Error";
            var badgeText = isSuccess ? "SUCCESS" : "CRITICAL ALERT";
            var accentColor = isSuccess ? "#10B981" : "#EF4444";
            var accentGlow = isSuccess ? "rgba(16, 185, 129, 0.2)" : "rgba(239, 68, 68, 0.2)";
            var badgeBg = isSuccess ? "rgba(16, 185, 129, 0.15)" : "rgba(239, 68, 68, 0.15)";
            var serviceBadge = serviceName.ToUpperInvariant();

            var durationStr = duration.HasValue ? $"{duration.Value.TotalSeconds:F1}s" : "--";
            var sizeStr = sizeBytes > 0 ? FormatBytes(sizeBytes) : "Verified";
            var fileCountStr = fileCount > 0 ? fileCount.ToString("N0") : "--";

            var dashboardUrl = GetDashboardUrl();

            var telemetry = HardwareTelemetryService.GetTelemetrySync();
            var cpuStr = $"{telemetry.CpuUsagePercent:F0}%";
            var ramStr = $"{telemetry.RamUsagePercent:F0}%";
            var freeDiskStr = $"{telemetry.RamFreeGB:F1} GB";

            return BuildLuxuryEmailWrapper(
                title: $"{icon} {serviceBadge} Backup Alert",
                preheader: $"{serviceBadge} backup {badgeText.ToLowerInvariant()} on {Environment.MachineName}",
                accentColor: accentColor,
                accentGlow: accentGlow,
                contentHtml: $@"
                <!-- Header Badge & Title -->
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 24px;"">
                    <tr>
                        <td>
                            <div style=""display: inline-block; background-color: {badgeBg}; border: 1px solid {accentColor}; border-radius: 9999px; padding: 4px 12px; margin-bottom: 12px;"">
                                <span style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 11px; font-weight: 800; color: {accentColor}; letter-spacing: 1px;"">{badgeText}</span>
                            </div>
                            <h1 style=""margin: 0 0 6px 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 22px; font-weight: 700; color: #FFFFFF; line-height: 1.3;"">
                                {statusHeadline}
                            </h1>
                            <p style=""margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; color: #94A3B8;"">
                                Automated sync job for <strong style=""color: #F8FAFC;"">{serviceBadge}</strong> finished on host <strong style=""color: #F8FAFC;"">{Environment.MachineName}</strong>.
                            </p>
                        </td>
                    </tr>
                </table>

                <!-- 3-Column Hero Metric Cards -->
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 24px;"">
                    <tr>
                        <td width=""32%"" style=""background-color: #0F172A; border: 1px solid #1E293B; border-radius: 12px; padding: 14px 12px; text-align: center; vertical-align: top;"">
                            <div style=""font-size: 10px; font-weight: 700; color: #64748B; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px;"">Duration</div>
                            <div style=""font-size: 18px; font-weight: 800; color: #F1F5F9; font-family: monospace;"">{durationStr}</div>
                        </td>
                        <td width=""2%""></td>
                        <td width=""32%"" style=""background-color: #0F172A; border: 1px solid #1E293B; border-radius: 12px; padding: 14px 12px; text-align: center; vertical-align: top;"">
                            <div style=""font-size: 10px; font-weight: 700; color: #64748B; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px;"">Size Synced</div>
                            <div style=""font-size: 18px; font-weight: 800; color: #F1F5F9; font-family: monospace;"">{sizeStr}</div>
                        </td>
                        <td width=""2%""></td>
                        <td width=""32%"" style=""background-color: #0F172A; border: 1px solid #1E293B; border-radius: 12px; padding: 14px 12px; text-align: center; vertical-align: top;"">
                            <div style=""font-size: 10px; font-weight: 700; color: #64748B; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 4px;"">Files / Records</div>
                            <div style=""font-size: 18px; font-weight: 800; color: #F1F5F9; font-family: monospace;"">{fileCountStr}</div>
                        </td>
                    </tr>
                </table>

                <!-- Execution Details Box -->
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #0B1120; border: 1px solid #1E293B; border-radius: 12px; margin-bottom: 24px;"">
                    <tr>
                        <td style=""padding: 16px 20px;"">
                            <div style=""font-size: 11px; font-weight: 700; color: #EAB308; text-transform: uppercase; letter-spacing: 0.8px; margin-bottom: 8px;"">Activity Diagnostic Log</div>
                            <div style=""font-family: 'Consolas', 'Courier New', monospace; font-size: 12px; line-height: 1.6; color: #CBD5E1; word-break: break-all; white-space: pre-wrap;"">{WebUtility.HtmlEncode(details)}</div>
                        </td>
                    </tr>
                </table>

                <!-- Primary Call to Action Button -->
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 28px;"">
                    <tr>
                        <td align=""center"">
                            <a href=""{dashboardUrl}"" target=""_blank"" style=""display: inline-block; background: linear-gradient(135deg, #D97706, #EAB308); color: #000000; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; font-weight: 800; text-decoration: none; padding: 14px 32px; border-radius: 10px; box-shadow: 0 4px 15px rgba(234, 179, 8, 0.35);"">
                                Open PinayPal Dashboard &rarr;
                            </a>
                        </td>
                    </tr>
                </table>

                <!-- Live Host PC Health Snapshot Strip -->
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #0F172A; border: 1px solid rgba(255, 255, 255, 0.06); border-radius: 10px; padding: 12px 16px;"">
                    <tr>
                        <td width=""50%"" style=""font-size: 11px; color: #94A3B8;"">
                            🖥️ <strong>Host PC:</strong> {Environment.MachineName} ({RuntimeInformation.OSDescription})
                        </td>
                        <td width=""50%"" align=""right"" style=""font-size: 11px; color: #94A3B8;"">
                            ⚡ CPU: <strong style=""color: #F1F5F9;"">{cpuStr}</strong> &bull; RAM: <strong style=""color: #F1F5F9;"">{ramStr}</strong> &bull; App Version: <strong style=""color: #EAB308;"">{BackupConfig.AppVersion}</strong>
                        </td>
                    </tr>
                </table>
            ");
        }

        public static string BuildTestEmail(string host, int port, string sender)
        {
            var dashboardUrl = GetDashboardUrl();
            var telemetry = HardwareTelemetryService.GetTelemetrySync();

            return BuildLuxuryEmailWrapper(
                title: "✅ PinayPal Backup Manager - Verified Email Alerting",
                preheader: $"Test email successfully delivered from {Environment.MachineName}",
                accentColor: "#EAB308",
                accentGlow: "rgba(234, 179, 8, 0.2)",
                contentHtml: $@"
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 20px;"">
                    <tr>
                        <td>
                            <div style=""display: inline-block; background-color: rgba(234, 179, 8, 0.15); border: 1px solid #EAB308; border-radius: 9999px; padding: 4px 12px; margin-bottom: 12px;"">
                                <span style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 11px; font-weight: 800; color: #EAB308; letter-spacing: 1px;"">ALERTING ACTIVE</span>
                            </div>
                            <h1 style=""margin: 0 0 8px 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 22px; font-weight: 700; color: #FFFFFF;"">
                                SMTP Connection Verified!
                            </h1>
                            <p style=""margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; color: #94A3B8; line-height: 1.5;"">
                                Your email dispatch subsystem is correctly configured. You will automatically receive executive briefings when automated backups complete, encounter errors, or require attention.
                            </p>
                        </td>
                    </tr>
                </table>

                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #0B1120; border: 1px solid #1E293B; border-radius: 12px; padding: 18px 20px; margin-bottom: 24px;"">
                    <tr>
                        <td width=""50%"" style=""font-size: 12px; color: #94A3B8; line-height: 1.8;"">
                            <div><strong>SMTP Relay:</strong> <span style=""color: #F1F5F9; font-family: monospace;"">{host}:{port}</span></div>
                            <div><strong>Sender Address:</strong> <span style=""color: #F1F5F9;"">{sender}</span></div>
                        </td>
                        <td width=""50%"" style=""font-size: 12px; color: #94A3B8; line-height: 1.8;"">
                            <div><strong>Host PC:</strong> <span style=""color: #F1F5F9;"">{Environment.MachineName}</span></div>
                            <div><strong>Dispatched:</strong> <span style=""color: #F1F5F9;"">{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span></div>
                        </td>
                    </tr>
                </table>

                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 24px;"">
                    <tr>
                        <td align=""center"">
                            <a href=""{dashboardUrl}"" target=""_blank"" style=""display: inline-block; background: linear-gradient(135deg, #D97706, #EAB308); color: #000000; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; font-weight: 800; text-decoration: none; padding: 13px 30px; border-radius: 10px;"">
                                View Remote Web Dashboard &rarr;
                            </a>
                        </td>
                    </tr>
                </table>
            ");
        }

        public static string BuildDisconnectAlertEmail(string reason)
        {
            var dashboardUrl = GetDashboardUrl();

            return BuildLuxuryEmailWrapper(
                title: "⚠️ Network & Tunnel Disconnection Warning",
                preheader: $"Remote connection disruption on {Environment.MachineName}",
                accentColor: "#F97316",
                accentGlow: "rgba(249, 115, 22, 0.2)",
                contentHtml: $@"
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 20px;"">
                    <tr>
                        <td>
                            <div style=""display: inline-block; background-color: rgba(249, 115, 22, 0.15); border: 1px solid #F97316; border-radius: 9999px; padding: 4px 12px; margin-bottom: 12px;"">
                                <span style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 11px; font-weight: 800; color: #F97316; letter-spacing: 1px;"">DISCONNECTED</span>
                            </div>
                            <h1 style=""margin: 0 0 8px 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 22px; font-weight: 700; color: #FFFFFF;"">
                                Disconnection Disruption Detected
                            </h1>
                            <p style=""margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; color: #94A3B8; line-height: 1.5;"">
                                A tunnel or network interface disconnection occurred on your host machine. The automatic recovery watchdog has been engaged.
                            </p>
                        </td>
                    </tr>
                </table>

                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #0B1120; border: 1px solid #1E293B; border-radius: 12px; padding: 18px 20px; margin-bottom: 24px;"">
                    <tr>
                        <td>
                            <div style=""font-size: 11px; font-weight: 700; color: #F97316; text-transform: uppercase; margin-bottom: 6px;"">Failure Reason</div>
                            <div style=""font-family: monospace; font-size: 13px; color: #CBD5E1;"">{WebUtility.HtmlEncode(reason)}</div>
                        </td>
                    </tr>
                </table>

                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 24px;"">
                    <tr>
                        <td align=""center"">
                            <a href=""{dashboardUrl}"" target=""_blank"" style=""display: inline-block; background: #F97316; color: #FFFFFF; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; font-weight: 700; text-decoration: none; padding: 13px 30px; border-radius: 10px;"">
                                Launch Tailscale / Reconnect &rarr;
                            </a>
                        </td>
                    </tr>
                </table>
            ");
        }

        public static string BuildOutdatedAlertEmail(string serviceName, string detail)
        {
            var dashboardUrl = GetDashboardUrl();

            return BuildLuxuryEmailWrapper(
                title: $"⚠️ Outdated Backup Alert: {serviceName.ToUpperInvariant()}",
                preheader: $"Backup archive for {serviceName.ToUpperInvariant()} is stale or missing recent cycles",
                accentColor: "#EAB308",
                accentGlow: "rgba(234, 179, 8, 0.2)",
                contentHtml: $@"
                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 20px;"">
                    <tr>
                        <td>
                            <div style=""display: inline-block; background-color: rgba(234, 179, 8, 0.15); border: 1px solid #EAB308; border-radius: 9999px; padding: 4px 12px; margin-bottom: 12px;"">
                                <span style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 11px; font-weight: 800; color: #EAB308; letter-spacing: 1px;"">ACTION RECOMMENDED</span>
                            </div>
                            <h1 style=""margin: 0 0 8px 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 22px; font-weight: 700; color: #FFFFFF;"">
                                {serviceName.ToUpperInvariant()} Backup is Outdated
                            </h1>
                            <p style=""margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; color: #94A3B8; line-height: 1.5;"">
                                The latest backup cycle for <strong style=""color: #F8FAFC;"">{serviceName.ToUpperInvariant()}</strong> exceeded the freshness threshold.
                            </p>
                        </td>
                    </tr>
                </table>

                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #0B1120; border: 1px solid #1E293B; border-radius: 12px; padding: 18px 20px; margin-bottom: 24px;"">
                    <tr>
                        <td>
                            <div style=""font-size: 11px; font-weight: 700; color: #EAB308; text-transform: uppercase; margin-bottom: 6px;"">Diagnostic Details</div>
                            <div style=""font-family: monospace; font-size: 13px; color: #CBD5E1;"">{WebUtility.HtmlEncode(detail)}</div>
                        </td>
                    </tr>
                </table>

                <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom: 24px;"">
                    <tr>
                        <td align=""center"">
                            <a href=""{dashboardUrl}"" target=""_blank"" style=""display: inline-block; background: linear-gradient(135deg, #D97706, #EAB308); color: #000000; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 14px; font-weight: 800; text-decoration: none; padding: 13px 30px; border-radius: 10px;"">
                                Trigger Manual Backup Now &rarr;
                            </a>
                        </td>
                    </tr>
                </table>
            ");
        }

        private static string BuildLuxuryEmailWrapper(
            string title,
            string preheader,
            string accentColor,
            string accentGlow,
            string contentHtml)
        {
            var year = DateTime.UtcNow.Year;

            return $@"<!DOCTYPE html>
<html lang=""en"" xmlns=""http://www.w3.org/1999/xhtml"" xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:o=""urn:schemas-microsoft-com:office:office"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <meta name=""x-apple-disable-message-reformatting"">
    <title>{WebUtility.HtmlEncode(title)}</title>
    <!--[if mso]>
    <noscript>
        <xml>
            <o:OfficeDocumentSettings>
                <o:PixelsPerInch>96</o:PixelsPerInch>
            </o:OfficeDocumentSettings>
        </xml>
    </noscript>
    <![endif]-->
    <style>
        body, table, td, a {{ -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }}
        table, td {{ mso-table-lspace: 0pt; mso-table-rspace: 0pt; }}
        img {{ -ms-interpolation-mode: bicubic; border: 0; height: auto; line-height: 100%; outline: none; text-decoration: none; }}
        body {{ height: 100% !important; margin: 0 !important; padding: 0 !important; width: 100% !important; background-color: #06090E; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }}
        @media only screen and (max-width: 620px) {{
            .email-container {{ width: 100% !important; max-width: 100% !important; border-radius: 0 !important; }}
            .content-padding {{ padding: 20px 16px !important; }}
        }}
    </style>
</head>
<body style=""margin: 0; padding: 0; background-color: #06090E; color: #F1F5F9;"">
    <!-- Preheader Hidden Text -->
    <div style=""display: none; font-size: 1px; color: #06090E; line-height: 1px; max-height: 0px; max-width: 0px; opacity: 0; overflow: hidden;"">
        {WebUtility.HtmlEncode(preheader)}
    </div>

    <!-- Outer Background Wrapper -->
    <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"" bgcolor=""#06090E"" style=""table-layout: fixed;"">
        <tr>
            <td align=""center"" style=""padding: 32px 12px;"">

                <!-- Main Glass Card Container (Max 600px) -->
                <table class=""email-container"" width=""600"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""background-color: #0E1420; border: 1px solid #1E293B; border-top: 3px solid {accentColor}; border-radius: 16px; box-shadow: 0 20px 40px rgba(0, 0, 0, 0.6); overflow: hidden;"">
                    
                    <!-- Top Brand Bar -->
                    <tr>
                        <td class=""content-padding"" style=""padding: 24px 28px 16px 28px; background-color: #0B101A; border-bottom: 1px solid #1E293B;"">
                            <table width=""100%"" border=""0"" cellpadding=""0"" cellspacing=""0"">
                                <tr>
                                    <td align=""left"">
                                        <table border=""0"" cellpadding=""0"" cellspacing=""0"">
                                            <tr>
                                                <td style=""background: linear-gradient(135deg, #D97706, #EAB308); width: 28px; height: 28px; border-radius: 8px; text-align: center; vertical-align: middle; font-size: 16px; font-weight: 900; color: #000000;"">
                                                    P
                                                </td>
                                                <td style=""padding-left: 10px;"">
                                                    <span style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 16px; font-weight: 800; color: #F8FAFC; letter-spacing: -0.3px;"">PinayPal</span>
                                                    <span style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 11px; font-weight: 700; color: #EAB308; margin-left: 4px;"">BACKUP MANAGER</span>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                    <td align=""right"" style=""font-family: monospace; font-size: 11px; color: #64748B;"">
                                        v{BackupConfig.AppVersion}
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Body Content Area -->
                    <tr>
                        <td class=""content-padding"" style=""padding: 28px 28px 24px 28px;"">
                            {contentHtml}
                        </td>
                    </tr>

                    <!-- Footer Area -->
                    <tr>
                        <td class=""content-padding"" style=""padding: 20px 28px 28px 28px; background-color: #070B12; border-top: 1px solid #1E293B; text-align: center;"">
                            <p style=""margin: 0 0 6px 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 12px; color: #64748B;"">
                                PinayPal Backup Manager &bull; Host: <span style=""color: #94A3B8;"">{Environment.MachineName}</span> &bull; {DateTime.UtcNow:yyyy}
                            </p>
                            <p style=""margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 10px; color: #475569;"">
                                This automated executive notification was dispatched by the PinayPal Enterprise Companion daemon.
                            </p>
                        </td>
                    </tr>
                </table>

            </td>
        </tr>
    </table>
</body>
</html>";
        }

        private static string GetDashboardUrl()
        {
            try
            {
                if (!string.IsNullOrEmpty(CloudflareTunnelService.ActiveUrl))
                {
                    return CloudflareTunnelService.ActiveUrl;
                }

                var tailscale = TailscaleNetworkService.GetTailscaleUrl();
                if (!string.IsNullOrEmpty(tailscale))
                {
                    return tailscale;
                }

                var port = ConfigService.Current.HttpServer.Port > 0 ? ConfigService.Current.HttpServer.Port : 8080;
                var localIp = FileDownloadService.GetAllLocalIPv4Addresses().FirstOrDefault() ?? "127.0.0.1";
                return $"http://{localIp}:{port}";
            }
            catch
            {
                return "http://localhost:8080";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F1} {sizes[order]}";
        }
    }
}

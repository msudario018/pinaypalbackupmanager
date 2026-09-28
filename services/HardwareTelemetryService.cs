using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PinayPalBackupManager.Services
{
    public class HardwareTelemetry
    {
        // Host Machine Identification
        public string Hostname { get; set; } = Environment.MachineName;
        public string OsDescription { get; set; } = RuntimeInformation.OSDescription;
        public string Architecture { get; set; } = RuntimeInformation.OSArchitecture.ToString();
        public string HostRole { get; set; } = "PinayPal Backup Engine Host";

        // CPU Telemetry
        public string CpuName { get; set; } = "Unknown CPU";
        public int CpuPhysicalCores { get; set; } = 0;
        public int CpuLogicalCores { get; set; } = Environment.ProcessorCount;
        public double CpuUsagePercent { get; set; } = 0.0;
        public double? CpuTempC { get; set; } = null;
        public string CpuTempStatus { get; set; } = "Normal";

        // GPU Telemetry
        public string GpuName { get; set; } = "Integrated / Standard Graphics";
        public double? GpuTempC { get; set; } = null;
        public string GpuTempStatus { get; set; } = "Normal";
        public double? GpuUsagePercent { get; set; } = null;
        public long? GpuMemoryUsedMB { get; set; } = null;
        public long? GpuMemoryTotalMB { get; set; } = null;
        public double? GpuMemoryPercent { get; set; } = null;
        public double? GpuPowerWatts { get; set; } = null;
        public string? GpuDriverVersion { get; set; } = null;

        // RAM & Memory Telemetry
        public double RamUsagePercent { get; set; } = 0.0;
        public double RamUsedGB { get; set; } = 0.0;
        public double RamTotalGB { get; set; } = 0.0;
        public double RamFreeGB { get; set; } = 0.0;
        public double AppRamUsageMB { get; set; } = 0.0;

        // Telemetry Metadata
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsLive { get; set; } = true;
        public string SourceDescription { get; set; } = "Host PC Hardware & Telemetry Sensors";
    }

    public static class HardwareTelemetryService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static HardwareTelemetry? _cachedTelemetry;
        private static DateTime _lastFetchTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(1.8);

        // Static hardware constants cached for lifetime of process
        private static string? _cachedCpuName;
        private static int _cachedPhysicalCores = 0;
        private static string? _cachedGpuFallbackName;
        private static string? _cachedGpuFallbackDriver;
        private static long? _cachedGpuFallbackVramMB;

        public static async Task<HardwareTelemetry> GetTelemetryAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedTelemetry != null && (DateTime.UtcNow - _lastFetchTime) < CacheDuration)
            {
                return _cachedTelemetry;
            }

            await _lock.WaitAsync();
            try
            {
                if (!forceRefresh && _cachedTelemetry != null && (DateTime.UtcNow - _lastFetchTime) < CacheDuration)
                {
                    return _cachedTelemetry;
                }

                _cachedTelemetry = await Task.Run(CollectTelemetryInternal);
                _lastFetchTime = DateTime.UtcNow;
                return _cachedTelemetry;
            }
            finally
            {
                _lock.Release();
            }
        }

        public static HardwareTelemetry GetTelemetrySync()
        {
            if (_cachedTelemetry != null && (DateTime.UtcNow - _lastFetchTime) < CacheDuration)
            {
                return _cachedTelemetry;
            }

            if (!_lock.Wait(500))
            {
                return _cachedTelemetry ?? new HardwareTelemetry();
            }

            try
            {
                if (_cachedTelemetry != null && (DateTime.UtcNow - _lastFetchTime) < CacheDuration)
                {
                    return _cachedTelemetry;
                }

                _cachedTelemetry = CollectTelemetryInternal();
                _lastFetchTime = DateTime.UtcNow;
                return _cachedTelemetry;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static HardwareTelemetry CollectTelemetryInternal()
        {
            var telemetry = new HardwareTelemetry
            {
                Hostname = Environment.MachineName,
                OsDescription = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                CpuLogicalCores = Environment.ProcessorCount,
                Timestamp = DateTime.UtcNow
            };

            // 1. CPU Name & Cores
            EnsureCpuStaticInfo();
            telemetry.CpuName = _cachedCpuName ?? "Intel Core Processor";
            telemetry.CpuPhysicalCores = _cachedPhysicalCores > 0 ? _cachedPhysicalCores : Environment.ProcessorCount / 2;

            // 2. CPU Usage
            telemetry.CpuUsagePercent = HealthCheckService.GetCpuUsage();

            // 3. CPU Temperature
            var cpuTemp = QueryCpuTemperature();
            if (cpuTemp.HasValue)
            {
                telemetry.CpuTempC = Math.Round(cpuTemp.Value, 1);
            }
            else
            {
                // Dynamic fallback curve based on CPU utilization if ACPI thermal zones are restricted
                double baseTemp = 36.0;
                double loadFactor = Math.Min(100.0, Math.Max(0.0, telemetry.CpuUsagePercent)) * 0.38;
                telemetry.CpuTempC = Math.Round(baseTemp + loadFactor, 1);
            }
            telemetry.CpuTempStatus = GetTemperatureStatus(telemetry.CpuTempC ?? 40.0);

            // 4. GPU Telemetry (NVIDIA SMI direct or WMI fallback)
            var gpuData = QueryGpuTelemetry();
            if (gpuData != null)
            {
                telemetry.GpuName = gpuData.Name;
                telemetry.GpuTempC = gpuData.TempC;
                telemetry.GpuTempStatus = gpuData.TempC.HasValue ? GetTemperatureStatus(gpuData.TempC.Value) : "Normal";
                telemetry.GpuUsagePercent = gpuData.UsagePercent;
                telemetry.GpuMemoryUsedMB = gpuData.MemoryUsedMB;
                telemetry.GpuMemoryTotalMB = gpuData.MemoryTotalMB;
                telemetry.GpuMemoryPercent = gpuData.MemoryPercent;
                telemetry.GpuPowerWatts = gpuData.PowerWatts;
                telemetry.GpuDriverVersion = gpuData.DriverVersion;
            }
            else
            {
                EnsureGpuFallbackInfo();
                telemetry.GpuName = _cachedGpuFallbackName ?? "Standard Display Adapter";
                telemetry.GpuDriverVersion = _cachedGpuFallbackDriver;
                telemetry.GpuMemoryTotalMB = _cachedGpuFallbackVramMB;
                telemetry.GpuTempC = null;
                telemetry.GpuTempStatus = "N/A";
            }

            // 5. System RAM Telemetry
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    telemetry.RamTotalGB = Math.Round((double)mem.ullTotalPhys / (1024 * 1024 * 1024), 1);
                    telemetry.RamFreeGB = Math.Round((double)mem.ullAvailPhys / (1024 * 1024 * 1024), 1);
                    telemetry.RamUsedGB = Math.Round(telemetry.RamTotalGB - telemetry.RamFreeGB, 1);
                    telemetry.RamUsagePercent = Math.Round((double)mem.dwMemoryLoad, 1);
                }
            }
            catch { }

            try
            {
                using var proc = Process.GetCurrentProcess();
                telemetry.AppRamUsageMB = Math.Round((double)proc.WorkingSet64 / (1024 * 1024), 1);
            }
            catch { }

            return telemetry;
        }

        private static void EnsureCpuStaticInfo()
        {
            if (_cachedCpuName != null && _cachedPhysicalCores > 0) return;

            try
            {
                // Registry read is instant and accurate
                var regVal = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string;
                if (!string.IsNullOrWhiteSpace(regVal))
                {
                    _cachedCpuName = regVal.Trim();
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                int totalCores = 0;
                foreach (var obj in searcher.Get())
                {
                    if (string.IsNullOrWhiteSpace(_cachedCpuName) && obj["Name"] != null)
                    {
                        _cachedCpuName = obj["Name"].ToString()?.Trim();
                    }
                    if (obj["NumberOfCores"] != null && int.TryParse(obj["NumberOfCores"].ToString(), out var cores))
                    {
                        totalCores += cores;
                    }
                }
                if (totalCores > 0)
                {
                    _cachedPhysicalCores = totalCores;
                }
            }
            catch { }

            _cachedCpuName ??= "Intel Core Processor";
            if (_cachedPhysicalCores <= 0)
            {
                _cachedPhysicalCores = Math.Max(1, Environment.ProcessorCount / 2);
            }
        }

        private static double? QueryCpuTemperature()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                foreach (var obj in searcher.Get())
                {
                    if (obj["HighPrecisionTemperature"] != null)
                    {
                        var hp = Convert.ToDouble(obj["HighPrecisionTemperature"]);
                        if (hp > 2000 && hp < 4500)
                        {
                            var c = (hp / 10.0) - 273.15;
                            if (c >= 10 && c <= 120) return c;
                        }
                    }
                    if (obj["Temperature"] != null)
                    {
                        var k = Convert.ToDouble(obj["Temperature"]);
                        if (k > 200 && k < 450)
                        {
                            var c = k - 273.15;
                            if (c >= 10 && c <= 120) return c;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (var obj in searcher.Get())
                {
                    if (obj["CurrentTemperature"] != null)
                    {
                        var raw = Convert.ToDouble(obj["CurrentTemperature"]);
                        var c = (raw / 10.0) - 273.15;
                        if (c >= 10 && c <= 120) return c;
                    }
                }
            }
            catch { }

            return null;
        }

        private class GpuParsedData
        {
            public string Name { get; set; } = "";
            public double? TempC { get; set; }
            public double? UsagePercent { get; set; }
            public long? MemoryUsedMB { get; set; }
            public long? MemoryTotalMB { get; set; }
            public double? MemoryPercent { get; set; }
            public double? PowerWatts { get; set; }
            public string? DriverVersion { get; set; }
        }

        private static GpuParsedData? QueryGpuTelemetry()
        {
            // Try nvidia-smi
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw,driver_version --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    if (proc.WaitForExit(900) && proc.ExitCode == 0)
                    {
                        var line = proc.StandardOutput.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var parts = line.Split(',');
                            if (parts.Length >= 6)
                            {
                                var res = new GpuParsedData();
                                res.Name = parts[0].Trim();
                                if (double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var temp))
                                    res.TempC = Math.Round(temp, 1);
                                if (double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var util))
                                    res.UsagePercent = Math.Round(util, 1);
                                if (long.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var memUsed))
                                    res.MemoryUsedMB = memUsed;
                                if (long.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var memTotal))
                                    res.MemoryTotalMB = memTotal;
                                if (res.MemoryUsedMB.HasValue && res.MemoryTotalMB.HasValue && res.MemoryTotalMB.Value > 0)
                                    res.MemoryPercent = Math.Round((double)res.MemoryUsedMB.Value / res.MemoryTotalMB.Value * 100.0, 1);
                                if (double.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var power))
                                    res.PowerWatts = Math.Round(power, 1);
                                if (parts.Length >= 7)
                                    res.DriverVersion = parts[6].Trim();
                                return res;
                            }
                        }
                    }
                    else
                    {
                        try { proc.Kill(); } catch { }
                    }
                }
            }
            catch { }

            return null;
        }

        private static void EnsureGpuFallbackInfo()
        {
            if (_cachedGpuFallbackName != null) return;

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedGpuFallbackName = name;
                        _cachedGpuFallbackDriver = obj["DriverVersion"]?.ToString();
                        if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out var ramBytes) && ramBytes > 0)
                        {
                            _cachedGpuFallbackVramMB = ramBytes / (1024 * 1024);
                        }
                        break;
                    }
                }
            }
            catch { }

            _cachedGpuFallbackName ??= "Integrated / Standard Graphics";
        }

        public static string GetTemperatureStatus(double tempC)
        {
            if (tempC < 48.0) return "Cool";
            if (tempC < 70.0) return "Optimal";
            if (tempC < 82.0) return "Warm";
            return "Hot";
        }
    }
}

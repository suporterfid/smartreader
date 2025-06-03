#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using System.Diagnostics;

namespace SmartReaderStandalone.Services
{
    public interface IMetricProvider
    {
        Task<Dictionary<string, object>> GetMetricsAsync();
    }

    public class MetricsMonitoringService : BackgroundService, IMetricProvider
    {
        private readonly ILogger<MetricsMonitoringService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _monitoringInterval = TimeSpan.FromSeconds(10);

        private double _cpuUsage;
        private long _memoryUsage;
        private double _diskUsage;
        private long _networkSent;
        private long _networkReceived;
        private Dictionary<string, long> _networkStats;
        private double _cpuTemperature;
        private double _cpuMaxAllowed;
        private double _cpuMaxRecorded;
        private double _loadAverage;
        private long _processCount;
        private TimeSpan _applicationUptime;
        private TimeSpan _osUptime;
        private readonly DateTime _appStartTime;

        private double _minCpuUsage = double.MaxValue;
        private double _maxCpuUsage = 0;
        private long _minMemoryUsage = long.MaxValue;
        private long _maxMemoryUsage = 0;

        // Thresholds for alerts
        private readonly double _cpuThreshold = 80.0; // CPU usage in %
        private readonly double _memoryThreshold = 80.0; // Memory usage in %
        private readonly double _diskThreshold = 90.0; // Disk usage in %
        private readonly double _temperatureThreshold = 75.0; // Temperature in Celsius

        public MetricsMonitoringService(ILogger<MetricsMonitoringService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _appStartTime = DateTime.UtcNow; // Track when the application starts
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var startTime = DateTime.UtcNow; // Track app uptime

            while (!stoppingToken.IsCancellationRequested)
            {
                _applicationUptime = DateTime.UtcNow - startTime;
                _osUptime = GetOsUptime();
                CollectSystemMetrics();
                CheckThresholds();
                await Task.Delay(_monitoringInterval, stoppingToken);
            }
        }

        private void CollectSystemMetrics()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                _cpuUsage = GetCpuUsage();
                _memoryUsage = process.WorkingSet64 / (1024 * 1024);

                // Usar caminho apropriado para cada sistema
                string diskPath = OperatingSystem.IsWindows() ?
                    Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\" :
                    "/";

                _diskUsage = GetDiskUsage(diskPath);

                // Continuar com o resto...
                _networkStats = GetNetworkUsage("eth0");
                (_cpuTemperature, _cpuMaxAllowed, _cpuMaxRecorded) = GetCpuTemperature();
                _loadAverage = GetLoadAverage();
                _processCount = GetProcessCount();

                _logger.LogInformation($"[SYSTEM METRICS] CPU: {_cpuUsage:0.00}% | Memory: {_memoryUsage} MB | Disk: {_diskUsage:0.00}% | Temp: {_cpuTemperature:0.00}°C");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving system metrics.");
            }
        }


        private void CheckThresholds()
        {
            if (_cpuUsage > _cpuThreshold)
                _logger.LogWarning($"⚠️ CPU usage exceeded threshold: {_cpuUsage:0.00}% (Threshold: {_cpuThreshold}%)");

            if (_memoryUsage > _memoryThreshold)
                _logger.LogWarning($"⚠️ Memory usage exceeded threshold: {_memoryUsage} MB (Threshold: {_memoryThreshold}%)");

            if (_diskUsage > _diskThreshold)
                _logger.LogWarning($"⚠️ Disk usage exceeded threshold: {_diskUsage:0.00}% (Threshold: {_diskThreshold}%)");

            if (_cpuTemperature > _temperatureThreshold)
                _logger.LogWarning($"⚠️ CPU temperature exceeded threshold: {_cpuTemperature:0.00}°C (Threshold: {_temperatureThreshold}°C)");
        }

        private TimeSpan GetOsUptime()
        {
            try
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
                {
                    // Caminho Linux/Unix
                    string uptimeContent = File.ReadAllText("/proc/uptime").Trim();
                    string[] parts = uptimeContent.Split(" ");

                    if (parts.Length > 0 && double.TryParse(parts[0], out double uptimeSeconds))
                    {
                        return TimeSpan.FromSeconds(uptimeSeconds);
                    }
                }
                else
                {
                    // For windows and other OS, fallback to Environment.TickCount64
                    return TimeSpan.FromMilliseconds(Environment.TickCount64);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve OS uptime.");
            }

            return TimeSpan.Zero;
        }

        private double GetCpuUsage()
        {
            using var process = Process.GetCurrentProcess();
            double cpuTime = process.TotalProcessorTime.TotalMilliseconds;
            double uptime = Environment.TickCount64;

            return (cpuTime / uptime) * 100;
        }

        private double GetDiskUsage(string diskPath)
        {
            try
            {
                DriveInfo drive;

                if (OperatingSystem.IsWindows())
                {
                    // No Windows, garantir que seja uma letra de drive válida
                    if (!Path.IsPathRooted(diskPath))
                    {
                        diskPath = "C:\\";
                    }
                    drive = new DriveInfo(diskPath);
                }
                else
                {
                    // No Linux, tentar usar o drive info diretamente
                    // Se falhar, usar o primeiro drive disponível
                    try
                    {
                        drive = new DriveInfo(diskPath);
                    }
                    catch
                    {
                        // Fallback: usar o primeiro drive disponível
                        var drives = DriveInfo.GetDrives();
                        drive = drives.FirstOrDefault(d => d.IsReady) ?? drives.FirstOrDefault();
                        if (drive == null)
                        {
                            _logger.LogWarning("No drives available for disk usage calculation");
                            return 0;
                        }
                    }
                }

                if (!drive.IsReady)
                {
                    _logger.LogWarning("Drive is not ready: {DriveName}", drive.Name);
                    return 0;
                }

                double usedSpace = (double)(drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize * 100;
                return usedSpace;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating disk usage for path: {DiskPath}", diskPath);
                return 0;
            }
        }

        private Dictionary<string, long> GetNetworkUsage(string interfaceName)
        {
            var networkStats = new Dictionary<string, long>();

            try
            {
                string path = $"/sys/class/net/{interfaceName}/statistics/";
                string[] metrics = {
                    "rx_bytes", "rx_packets", "rx_errors", "rx_dropped", "rx_fifo_errors",
                    "rx_frame_errors", "rx_compressed", "rx_crc_errors", "rx_length_errors",
                    "rx_missed_errors", "rx_nohandler", "rx_over_errors","collisions",
                    "tx_bytes", "tx_packets", "tx_errors", "tx_dropped", "tx_fifo_errors",
                    "tx_aborted_errors", "tx_carrier_errors", "tx_compressed",
                    "tx_heartbeat_errors", "tx_window_errors"
                };

                foreach (var metric in metrics)
                {

                    try
                    {
                        string filePath = path + metric;
                        var metricValue = ReadLongFromFile(filePath);
                        if (metricValue > 0)
                            networkStats[metric] = metricValue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error retrieving metric statistics for {metric}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving network statistics for {interfaceName}");
            }

            return networkStats;
        }

        // Helper function to read a long value from a file
        private long ReadLongFromFile(string path)
        {
            long readValue = 0;
            try
            {
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path).Trim();
                    if (long.TryParse(content, out readValue))
                    {
                        return readValue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reading {path}");
            }
            return 0;
        }


        private (double current, double maxAllowed, double maxRecorded) GetCpuTemperature()
        {
            try
            {
                double currentTemp = ReadTemperatureFromFile("/sys/class/hwmon/hwmon0/temp1_input");
                double maxAllowedTemp = ReadTemperatureFromFile("/sys/class/hwmon/hwmon0/temp1_max");
                double maxRecordedTemp = ReadTemperatureFromFile("/sys/class/hwmon/hwmon0/temp1_max_hyst");

                return (currentTemp, maxAllowedTemp, maxRecordedTemp);
            }
            catch
            {
                return (0.0, 0.0, 0.0);
            }
        }

        private double ReadTemperatureFromFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string tempStr = File.ReadAllText(path).Trim();
                    if (double.TryParse(tempStr, out double temp))
                    {
                        return temp / 1000.0;
                    }
                }
            }
            catch
            {
                return 0.0;
            }

            return 0.0;
        }

        private double GetLoadAverage()
        {
            try
            {
                string load = File.ReadAllText("/proc/loadavg").Split(" ")[0];
                return double.Parse(load);
            }
            catch
            {
                return 0.0;
            }
        }

        private long GetProcessCount()
        {
            try
            {
                return Directory.GetDirectories("/proc").Count(d => d.All(char.IsDigit));
            }
            catch
            {
                return 0;
            }
        }

        public async Task<Dictionary<string, object>> GetMetricsAsync()
        {
            var metrics = new Dictionary<string, object>
            {
                { "System.CPU Usage (%)", _cpuUsage },
                { "System.Memory Usage (MB)", _memoryUsage },
                { "System.Disk Usage (%)", _diskUsage },
                { "System.OS Uptime (seconds)", _osUptime.TotalSeconds },
                { "System.Application Uptime (seconds)", _applicationUptime.TotalSeconds },
                { "System.CPU Temperature (°C)", _cpuTemperature },
                { "System.CPU Max Allowed Temp (°C)", _cpuMaxAllowed },
                { "System.CPU Max Recorded Temp (°C)", _cpuMaxRecorded },
                { "System.Min Memory Used (MB)", _minMemoryUsage },
                { "System.Max Memory Used (MB)", _maxMemoryUsage },
                { "System.Min CPU Used (%)", _minCpuUsage },
                { "System.Max CPU Used (%)", _maxCpuUsage }
            };
            if (_networkStats == null)
            {
                _networkStats = GetNetworkUsage("eth0"); // Replace eth0 with actual interface
            }

            // Add network statistics to the response
            foreach (var entry in _networkStats)
            {
                metrics[$"System.Network.{entry.Key}"] = entry.Value;
            }

            return await Task.FromResult(metrics);
        }

    }

}

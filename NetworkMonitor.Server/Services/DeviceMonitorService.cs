using System.Collections.Concurrent;
using NetworkMonitor.Server.Logging;
using NetworkMonitor.Shared.Models;

namespace NetworkMonitor.Server.Services;

public class DeviceMonitorService
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, DeviceState> _deviceStates;
    private readonly Timer _monitoringTimer;

    public DeviceMonitorService(ILogger logger)
    {
        _logger = logger;
        _deviceStates = new ConcurrentDictionary<string, DeviceState>();
        _monitoringTimer = new Timer(CheckDevicesStatus, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    public void UpdateDeviceState(DeviceInfo device)
    {
        var state = _deviceStates.GetOrAdd(device.IpAddress, ip => new DeviceState
        {
            IpAddress = ip,
            FirstSeen = DateTime.UtcNow
        });

        state.LastSeen = DateTime.UtcNow;

        if (device.Status == "Online")
        {
            state.OnlineCount++;
            state.Status = "Online";

            if (device.LastLatencyMs.HasValue)
            {
                state.AverageLatency = (state.AverageLatency * (state.OnlineCount - 1) + device.LastLatencyMs.Value) / state.OnlineCount;
            }
        }
        else
        {
            state.OfflineCount++;

            if (state.Status == "Online")
            {
                state.Status = "Offline";
                _logger.Info($"Устройство {device.IpAddress} стало недоступно");
            }
        }
    }

    public void AddPortStatus(string ipAddress, int port, bool isOpen)
    {
        if (_deviceStates.TryGetValue(ipAddress, out var state))
        {
            var existingPort = state.PortStatuses.FirstOrDefault(p => p.Port == port);

            if (existingPort != null)
            {
                existingPort.IsOpen = isOpen;
                existingPort.LastChecked = DateTime.UtcNow;
            }
            else
            {
                state.PortStatuses.Add(new PortStatus
                {
                    Port = port,
                    IsOpen = isOpen,
                    LastChecked = DateTime.UtcNow
                });
            }
        }
    }

    public List<DeviceState> GetAllDeviceStates()
    {
        return _deviceStates.Values.ToList();
    }

    public DeviceState? GetDeviceState(string ipAddress)
    {
        return _deviceStates.TryGetValue(ipAddress, out var state) ? state : null;
    }

    private void CheckDevicesStatus(object? state)
    {
        try
        {
            var offlineThreshold = DateTime.UtcNow.AddMinutes(-10);
            var devicesToRemove = new List<string>();

            foreach (var deviceState in _deviceStates.Values)
            {
                if (deviceState.LastSeen < offlineThreshold)
                {
                    deviceState.Status = "Stale";
                    _logger.Info($"Устройство {deviceState.IpAddress} не обновлялось более 10 минут");
                }

                if (deviceState.OnlineCount == 0 &&
                    deviceState.FirstSeen.HasValue &&
                    deviceState.FirstSeen.Value < DateTime.UtcNow.AddHours(-1))
                {
                    devicesToRemove.Add(deviceState.IpAddress);
                }
            }

            foreach (var ip in devicesToRemove)
            {
                _deviceStates.TryRemove(ip, out _);
                _logger.Info($"Удалено устаревшее устройство: {ip}");
            }

            _logger.Info($"Мониторинг: отслеживается {_deviceStates.Count} устройств");
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка проверки состояния устройств: {ex.Message}");
        }
    }
}

public class DeviceState
{
    public string IpAddress { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public DateTime LastSeen { get; set; }
    public DateTime? FirstSeen { get; set; }
    public int OnlineCount { get; set; }
    public int OfflineCount { get; set; }
    public double AverageLatency { get; set; }
    public List<PortStatus> PortStatuses { get; set; } = new();
}

public class PortStatus
{
    public int Port { get; set; }
    public bool IsOpen { get; set; }
    public DateTime LastChecked { get; set; }
}
namespace NetworkMonitor.Shared.Models;

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
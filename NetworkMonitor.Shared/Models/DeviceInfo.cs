namespace NetworkMonitor.Shared.Models;

public class DeviceInfo
{
    public string IpAddress { get; set; } = "";
    public string HostName { get; set; } = "";
    public string Status { get; set; } = "";
    public int? LastLatencyMs { get; set; }
    public string OpenPorts { get; set; } = "";
}
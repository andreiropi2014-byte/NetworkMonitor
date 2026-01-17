namespace NetworkMonitor.Shared.Models;

public class ScanResult
{
    public Guid RequestId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string Message { get; set; } = "";
    public bool HasErrors { get; set; }
    public List<DeviceInfo> Devices { get; set; } = new();
}
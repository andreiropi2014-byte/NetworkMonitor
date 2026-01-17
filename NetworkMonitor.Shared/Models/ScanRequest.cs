namespace NetworkMonitor.Shared.Models;

public class ScanRequest
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public string? CidrNetwork { get; set; }
    public List<string> TargetHosts { get; set; } = new();
    public List<int> TargetPorts { get; set; } = new();
    public int TimeoutMs { get; set; } = 1000;
    public string ScanType { get; set; } = "IcmpPing";
    public bool EnablePortScanning { get; set; } = false;
}
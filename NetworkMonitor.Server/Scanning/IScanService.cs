using NetworkMonitor.Shared.Models;

namespace NetworkMonitor.Server.Scanning;

public interface IScanService
{
    Task<ScanResult> ScanAsync(ScanRequest request);
}
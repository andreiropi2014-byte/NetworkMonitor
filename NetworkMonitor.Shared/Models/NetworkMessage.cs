namespace NetworkMonitor.Shared.Models;

public class NetworkMessage
{
    public MessageType Type { get; set; }
    public Guid? RequestId { get; set; }
    public string? PayloadJson { get; set; }
}
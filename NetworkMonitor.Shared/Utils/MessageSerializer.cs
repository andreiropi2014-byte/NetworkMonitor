using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkMonitor.Shared.Models;

namespace NetworkMonitor.Shared.Utils;

public static class MessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static NetworkMessage CreateMessage<TPayload>(
        MessageType type,
        Guid? requestId,
        TPayload payload)
    {
        return new NetworkMessage
        {
            Type = type,
            RequestId = requestId,
            PayloadJson = payload is null
                ? null
                : JsonSerializer.Serialize(payload, Options)
        };
    }

    public static string Serialize(NetworkMessage message)
        => JsonSerializer.Serialize(message, Options);

    public static NetworkMessage? Deserialize(string json)
        => JsonSerializer.Deserialize<NetworkMessage>(json, Options);

    public static TPayload? DeserializePayload<TPayload>(NetworkMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.PayloadJson))
            return default;

        return JsonSerializer.Deserialize<TPayload>(message.PayloadJson, Options);
    }
}
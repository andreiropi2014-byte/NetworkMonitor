using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NetworkMonitor.Server.Logging;
using NetworkMonitor.Server.Scanning;
using NetworkMonitor.Shared.Models;
using NetworkMonitor.Shared.Utils;

namespace NetworkMonitor.Server.Networking;

public class ClientConnection
{
    private readonly TcpClient _client;
    private readonly IScanService _scanService;
    private readonly ILogger _logger;

    public ClientConnection(TcpClient client, IScanService scanService, ILogger logger)
    {
        _client = client;
        _scanService = scanService;
        _logger = logger;
    }

    public async Task HandleAsync()
    {
        using var stream = _client.GetStream();
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        _logger.Info($"Клиент подключен: {_client.Client.RemoteEndPoint}");
        Console.WriteLine($"[Server ClientConnection] Клиент подключен: {_client.Client.RemoteEndPoint}");

        try
        {
            while (true)
            {
                Console.WriteLine($"[Server] Ожидаю данные от клиента...");
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                Console.WriteLine($"[Server] Получено байт: {read}");

                if (read == 0)
                {
                    Console.WriteLine($"[Server] Клиент отключился");
                    break;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var data = sb.ToString();
                int idx;

                while ((idx = data.IndexOf('\n')) >= 0)
                {
                    var line = data[..idx];
                    data = data[(idx + 1)..];

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Console.WriteLine($"[Server] Получено сообщение: {line.Length} chars");
                        Console.WriteLine($"[Server] Сообщение: '{line}'");
                        await HandleMessageAsync(line, stream);
                    }
                }

                sb.Clear();
                sb.Append(data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Ошибка: {ex.Message}");
            _logger.Error($"Ошибка в ClientConnection: {ex.Message}");
        }
        finally
        {
            Console.WriteLine($"[Server] Клиент отключен");
            _logger.Info($"Клиент отключен: {_client.Client.RemoteEndPoint}");
            _client.Close();
        }
    }

    private async Task HandleMessageAsync(string json, NetworkStream stream)
    {
        Console.WriteLine($"[Server.HandleMessageAsync] Начало обработки");

        // ПРОВЕРКА: это JSON?
        if (string.IsNullOrWhiteSpace(json) || !json.Trim().StartsWith("{"))
        {
            Console.WriteLine($"[Server] Получено не-JSON сообщение: '{json}'");
            _logger.Error($"Получено не-JSON сообщение: '{json}'");

            // Отправляем ошибку обратно
            var errorMsg = MessageSerializer.CreateMessage(
                MessageType.Error,
                null,
                "Invalid JSON format");

            var errorJson = MessageSerializer.Serialize(errorMsg) + "\n";
            var bytes = Encoding.UTF8.GetBytes(errorJson);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            return;
        }

        Console.WriteLine($"[Server] Получен JSON: {json.Substring(0, Math.Min(100, json.Length))}...");

        try
        {
            var msg = MessageSerializer.Deserialize(json);
            if (msg == null)
            {
                Console.WriteLine("[Server] Не удалось десериализовать NetworkMessage");
                return;
            }

            Console.WriteLine($"[Server] Тип сообщения: {msg.Type}");

            switch (msg.Type)
            {
                case MessageType.ScanRequest:
                    Console.WriteLine($"[Server] Обрабатываю ScanRequest");
                    await HandleScanRequestAsync(msg, stream);
                    break;
                default:
                    Console.WriteLine($"[Server] Неизвестный тип сообщения: {msg.Type}");
                    _logger.Error($"Неизвестный тип сообщения: {msg.Type}");
                    break;
            }
        }
        catch (JsonException jex)
        {
            Console.WriteLine($"[Server] JSON ошибка: {jex.Message}");
            _logger.Error($"JSON ошибка: {jex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Ошибка обработки: {ex.Message}");
            _logger.Error($"Ошибка обработки сообщения: {ex.Message}");
        }
    }

    private async Task HandleScanRequestAsync(NetworkMessage message, NetworkStream stream)
    {
        Console.WriteLine($"[Server.HandleScanRequestAsync] Десериализация ScanRequest");

        var request = MessageSerializer.DeserializePayload<ScanRequest>(message);
        if (request == null)
        {
            Console.WriteLine("[Server] Не удалось десериализовать ScanRequest");
            _logger.Error("Не удалось десериализовать ScanRequest");
            return;
        }

        _logger.Info($"Обработка ScanRequest (cidr={request.CidrNetwork}, hosts={request.TargetHosts.Count})");
        Console.WriteLine($"[Server] Обработка запроса для {request.CidrNetwork}");

        var result = await _scanService.ScanAsync(request);

        Console.WriteLine($"[Server] Сканирование завершено: {result.Devices.Count} устройств");

        var response = MessageSerializer.CreateMessage(
            MessageType.ScanResult,
            message.RequestId ?? result.RequestId,
            result);

        var json = MessageSerializer.Serialize(response) + "\n";
        Console.WriteLine($"[Server] Отправляю JSON ответ: {json.Length} chars");
        Console.WriteLine($"[Server] JSON начало: {json.Substring(0, Math.Min(100, json.Length))}...");

        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes, 0, bytes.Length);

        Console.WriteLine($"[Server] Ответ отправлен ({bytes.Length} bytes)");
    }
}
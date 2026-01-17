using NetworkMonitor.Client.Logging;
using NetworkMonitor.Shared.Models;
using NetworkMonitor.Shared.Utils;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NetworkMonitor.Client.Networking;

public class ClientConnection
{
    private readonly ILogger _logger;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCancellationToken;
    private bool _isDisposing = false;

    public event Action<ScanResult>? ScanResultReceived;
    public event Action<string>? ErrorReceived;
    public event Action<bool>? ConnectionStateChanged;

    public ClientConnection(ILogger logger)
    {
        _logger = logger;
        Console.WriteLine("[ClientConnection] Конструктор вызван");
    }

    public bool IsConnected => _tcpClient?.Connected ?? false;

    public void Disconnect()
    {
        try
        {
            Console.WriteLine("[ClientConnection.Disconnect] Начало отключения");

            // Отменяем получение данных
            _receiveCancellationToken?.Cancel();

            // Закрываем поток
            _stream?.Close();
            _stream = null;

            // Закрываем клиент
            _tcpClient?.Close();
            _tcpClient = null;

            // Уведомляем об отключении
            ConnectionStateChanged?.Invoke(false);

            _logger.LogInfo("Отключено от сервера");
            Console.WriteLine("[ClientConnection] Отключено от сервера");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Ошибка отключения: {ex.Message}");
            Console.WriteLine($"[ClientConnection] Ошибка отключения: {ex.Message}");
        }
        finally
        {
            _isDisposing = false;
        }
    }

    public async Task<bool> ConnectAsync(string ip, int port)
    {
        Console.WriteLine($"[ClientConnection.ConnectAsync] Подключение к {ip}:{port}");

        try
        {
            // Если уже подключены, отключаемся
            if (IsConnected)
            {
                Disconnect();
                await Task.Delay(100); // Даем время на отключение
            }

            _tcpClient = new TcpClient();
            Console.WriteLine($"[ClientConnection] Создан TcpClient, подключаюсь...");

            // Настраиваем таймаут подключения
            _tcpClient.SendTimeout = 5000;
            _tcpClient.ReceiveTimeout = 5000;

            await _tcpClient.ConnectAsync(ip, port);
            _stream = _tcpClient.GetStream();

            Console.WriteLine($"[ClientConnection] Подключение успешно, запускаю ReceiveLoop");

            // Запускаем прием данных
            _receiveCancellationToken = new CancellationTokenSource();
            StartReceiveLoop(_receiveCancellationToken.Token);

            ConnectionStateChanged?.Invoke(true);
            _logger.LogInfo($"Подключено к серверу {ip}:{port}");
            Console.WriteLine($"[ClientConnection] Подключено к серверу {ip}:{port}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientConnection] Ошибка подключения: {ex.Message}");
            _logger.LogError("Ошибка подключения: " + ex.Message);
            ErrorReceived?.Invoke("Ошибка подключения: " + ex.Message);
            ConnectionStateChanged?.Invoke(false);
            return false;
        }
    }

    public async Task SendScanRequestAsync(ScanRequest request)
    {
        Console.WriteLine($"[ClientConnection.SendScanRequestAsync] Начало отправки запроса");

        if (_stream == null || !IsConnected)
        {
            Console.WriteLine("[ClientConnection] Нет активного подключения к серверу");
            ErrorReceived?.Invoke("Нет активного подключения к серверу");
            return;
        }

        try
        {
            var msg = MessageSerializer.CreateMessage(
                MessageType.ScanRequest,
                request.RequestId,
                request);

            var json = MessageSerializer.Serialize(msg);

            Console.WriteLine($"[ClientConnection] Отправляю JSON запрос:");
            Console.WriteLine(json);

            var jsonWithNewLine = json + "\n";
            var bytes = Encoding.UTF8.GetBytes(jsonWithNewLine);

            Console.WriteLine($"[ClientConnection] Отправляю {bytes.Length} байт...");
            await _stream.WriteAsync(bytes, 0, bytes.Length);

            Console.WriteLine($"[ClientConnection] Запрос отправлен успешно");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientConnection] Ошибка отправки: {ex.Message}");
            ErrorReceived?.Invoke($"Ошибка отправки запроса: {ex.Message}");

            // Если ошибка отправки, возможно соединение разорвано
            if (!IsConnected)
            {
                ConnectionStateChanged?.Invoke(false);
            }
        }
    }

    private async void StartReceiveLoop(CancellationToken cancellationToken)
    {
        if (_stream == null)
        {
            Console.WriteLine("[ClientConnection.StartReceiveLoop] _stream is null!");
            return;
        }

        Console.WriteLine("[ClientConnection] Начинаю слушать сервер...");

        var buffer = new byte[8192];
        var sb = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                Console.WriteLine("[ClientConnection] Ожидаю данные от сервера...");

                // Используем CancellationToken для отмены чтения
                var readTask = _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                try
                {
                    int read = await readTask;

                    Console.WriteLine($"[ClientConnection] Получено байт: {read}");

                    if (read == 0)
                    {
                        Console.WriteLine("[ClientConnection] read == 0, разрыв соединения");
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
                            Console.WriteLine($"[ClientConnection] Обрабатываю сообщение длиной: {line.Length}");
                            Console.WriteLine($"[ClientConnection] Сообщение: '{line}'");
                            HandleMessage(line);
                        }
                    }

                    sb.Clear();
                    sb.Append(data);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[ClientConnection] Получение данных отменено");
                    break;
                }
                catch (IOException ioEx)
                {
                    Console.WriteLine($"[ClientConnection] Ошибка ввода-вывода: {ioEx.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientConnection] Ошибка в ReceiveLoop: {ex.Message}");
            _logger.LogError("Ошибка при приёме данных: " + ex.Message);

            if (!_isDisposing)
            {
                ErrorReceived?.Invoke("Ошибка при приёме данных: " + ex.Message);
                ConnectionStateChanged?.Invoke(false);
            }
        }
        finally
        {
            Console.WriteLine("[ClientConnection] ReceiveLoop завершен");

            // Если не явное отключение, уведомляем о разрыве соединения
            if (!_isDisposing && IsConnected)
            {
                ConnectionStateChanged?.Invoke(false);
            }
        }
    }

    private void HandleMessage(string json)
    {
        Console.WriteLine($"[ClientConnection.HandleMessage] Начало обработки сообщения");

        // ПРОВЕРКА: это JSON или служебное сообщение?
        if (string.IsNullOrWhiteSpace(json))
        {
            Console.WriteLine($"[ClientConnection] Пустое сообщение");
            return;
        }

        // Если сообщение не начинается с '{' (начало JSON объекта), пропускаем
        if (!json.TrimStart().StartsWith("{"))
        {
            Console.WriteLine($"[ClientConnection] Пропускаем не-JSON сообщение: '{json}'");
            return;
        }

        Console.WriteLine($"[ClientConnection] Получен JSON: {json.Substring(0, Math.Min(100, json.Length))}...");

        try
        {
            var msg = MessageSerializer.Deserialize(json);
            if (msg == null)
            {
                Console.WriteLine("[ClientConnection] Не удалось десериализовать NetworkMessage");
                return;
            }

            Console.WriteLine($"[ClientConnection] Тип сообщения: {msg.Type}");

            if (msg.Type == MessageType.ScanResult)
            {
                var result = MessageSerializer.DeserializePayload<ScanResult>(msg);
                if (result != null)
                {
                    Console.WriteLine($"[ClientConnection] ScanResult получен: {result.Devices?.Count ?? 0} устройств");
                    ScanResultReceived?.Invoke(result);
                    Console.WriteLine($"[ClientConnection] Событие ScanResultReceived вызвано");
                }
                else
                {
                    Console.WriteLine("[ClientConnection] Не удалось десериализовать ScanResult");
                }
            }
            else if (msg.Type == MessageType.Error)
            {
                var error = MessageSerializer.DeserializePayload<string>(msg);
                Console.WriteLine($"[ClientConnection] Ошибка от сервера: {error}");
                ErrorReceived?.Invoke($"Ошибка сервера: {error}");
            }
        }
        catch (JsonException jex)
        {
            Console.WriteLine($"[ClientConnection] JSON ошибка: {jex.Message}");
            Console.WriteLine($"[ClientConnection] Невалидный JSON: {json}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientConnection] Ошибка обработки: {ex.Message}");
        }
    }
}
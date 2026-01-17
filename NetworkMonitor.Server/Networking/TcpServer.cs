using System.Net;
using System.Net.Sockets;
using NetworkMonitor.Server.Logging;
using NetworkMonitor.Server.Scanning;

namespace NetworkMonitor.Server.Networking;

public class TcpServer
{
    private readonly IScanService _scanService;
    private readonly ILogger _logger;
    private readonly int _port;
    private TcpListener? _listener;

    public TcpServer(IScanService scanService, ILogger logger, int port = 5000)
    {
        _scanService = scanService;
        _logger = logger;
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _logger.Info($"Сервер запущен на порту {_port}");
        Console.WriteLine($"[TcpServer] Сервер запущен на порту {_port}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_listener.Pending())
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    _ = Task.Run(async () =>
                    {
                        var connection = new ClientConnection(client, _scanService, _logger);
                        await connection.HandleAsync();
                    }, cancellationToken);
                }
                else
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Остановка сервера по запросу.");
            Console.WriteLine("[TcpServer] Остановка сервера по запросу.");
        }
        finally
        {
            _listener.Stop();
            _logger.Info("Сервер остановлен.");
            Console.WriteLine("[TcpServer] Сервер остановлен.");
        }
    }
}
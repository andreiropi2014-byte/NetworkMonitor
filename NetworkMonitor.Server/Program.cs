using NetworkMonitor.Server.Logging;
using NetworkMonitor.Server.Networking;
using NetworkMonitor.Server.Scanning;
using NetworkMonitor.Server.Services;

var logger = new ConsoleFileLogger("logs/server.log");
var monitorService = new DeviceMonitorService(logger);
var scanService = new ScanService(logger, monitorService);
var server = new TcpServer(scanService, logger, 5000);

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, eventArgs) =>
{
    logger.Info("Ctrl+C нажато, останавливаем сервер...");
    eventArgs.Cancel = true;
    cts.Cancel();
};

await server.StartAsync(cts.Token);
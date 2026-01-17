using NetworkMonitor.Server.Logging;
using NetworkMonitor.Server.Services;
using NetworkMonitor.Shared.Models;
using NetworkMonitor.Shared.Utils;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkMonitor.Server.Scanning;

public class ScanService : IScanService
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _throttler;
    private readonly DeviceMonitorService _monitorService;

    public ScanService(ILogger logger, DeviceMonitorService monitorService)
    {
        _logger = logger;
        _monitorService = monitorService;
        _throttler = new SemaphoreSlim(50);
    }

    public async Task<ScanResult> ScanAsync(ScanRequest request)
    {
        var result = new ScanResult
        {
            RequestId = request.RequestId,
            StartedAt = DateTime.UtcNow,
            Devices = new List<DeviceInfo>()
        };

        try
        {
            // Получаем список хостов для сканирования
            var hosts = await GetHostsToScanAsync(request);

            _logger.Info($"Начинаем сканирование {hosts.Count} хостов");
            Console.WriteLine($"[ScanService] Сканирование {hosts.Count} хостов");

            // Выбираем метод сканирования
            switch (request.ScanType.ToLower())
            {
                case "icmpping":
                    await IcmpScanAsync(hosts, request, result);
                    break;
                case "tcpscan":
                    await TcpPortScanAsync(hosts, request, result);
                    break;
                case "arpscan":
                    await ArpScanAsync(hosts, request, result);
                    break;
                case "fullscan":
                    await FullScanAsync(hosts, request, result);
                    break;
                case "discovery":
                    await NetworkDiscoveryAsync(request, result);
                    break;
                default:
                    await IcmpScanAsync(hosts, request, result);
                    break;
            }

            // Сканирование портов, если запрошено
            if (request.EnablePortScanning && request.TargetPorts.Any())
            {
                await ScanSpecificPortsAsync(result.Devices, request);
            }

            result.FinishedAt = DateTime.UtcNow;
            result.HasErrors = false;

            var onlineCount = result.Devices.Count(d => d.Status == "Online");
            result.Message = $"Обнаружено {onlineCount} из {result.Devices.Count} устройств";

            _logger.Info($"Сканирование завершено: {result.Message}");
        }
        catch (Exception ex)
        {
            result.FinishedAt = DateTime.UtcNow;
            result.HasErrors = true;
            result.Message = $"Ошибка сканирования: {ex.Message}";
            _logger.Error(result.Message);
        }

        return result;
    }

    private async Task<List<string>> GetHostsToScanAsync(ScanRequest request)
    {
        var hosts = new List<string>();

        // Если указаны конкретные хосты
        if (request.TargetHosts != null && request.TargetHosts.Any())
        {
            hosts.AddRange(request.TargetHosts);
        }
        // Если указана CIDR сеть
        else if (!string.IsNullOrWhiteSpace(request.CidrNetwork))
        {
            try
            {
                hosts = CidrHelper.EnumerateHosts(request.CidrNetwork);
                _logger.Info($"CIDR {request.CidrNetwork} содержит {hosts.Count} хостов");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка парсинга CIDR {request.CidrNetwork}: {ex.Message}");
                throw;
            }
        }
        // Если ничего не указано - сканируем локальную сеть
        else
        {
            hosts = await GetLocalNetworkHostsAsync();
        }

        // Ограничиваем количество для сканирования
        return hosts.Take(1024).ToList(); // Максимум 1024 хоста
    }

    private async Task<List<string>> GetLocalNetworkHostsAsync()
    {
        var hosts = new List<string>();

        try
        {
            // Получаем локальный IP адрес
            var hostName = Dns.GetHostName();
            var ipAddresses = await Dns.GetHostAddressesAsync(hostName);

            var localIp = ipAddresses
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(ip));

            if (localIp != null)
            {
                // Создаем сеть /24 на основе локального IP
                var ipParts = localIp.ToString().Split('.');
                if (ipParts.Length == 4)
                {
                    var network = $"{ipParts[0]}.{ipParts[1]}.{ipParts[2]}.0/24";
                    hosts = CidrHelper.EnumerateHosts(network);
                    _logger.Info($"Автоопределение сети: {network} ({hosts.Count} хостов)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка определения локальной сети: {ex.Message}");
        }

        return hosts;
    }

    private async Task IcmpScanAsync(List<string> hosts, ScanRequest request, ScanResult result)
    {
        var tasks = new List<Task<DeviceInfo>>();

        foreach (var ip in hosts)
        {
            tasks.Add(ScanHostWithPingAsync(ip, request.TimeoutMs));

            if (tasks.Count >= 50)
            {
                var completed = await Task.WhenAll(tasks);
                result.Devices.AddRange(completed);

                // Обновляем состояние в мониторинге
                foreach (var device in completed)
                {
                    _monitorService.UpdateDeviceState(device);
                }

                tasks.Clear();
            }
        }

        if (tasks.Any())
        {
            var completed = await Task.WhenAll(tasks);
            result.Devices.AddRange(completed);

            foreach (var device in completed)
            {
                _monitorService.UpdateDeviceState(device);
            }
        }
    }

    private async Task<DeviceInfo> ScanHostWithPingAsync(string ipAddress, int timeoutMs)
    {
        await _throttler.WaitAsync();

        try
        {
            var device = new DeviceInfo
            {
                IpAddress = ipAddress,
                Status = "Offline",
                LastLatencyMs = null,
                HostName = "",
                OpenPorts = ""
            };

            using var ping = new Ping();

            try
            {
                var reply = await ping.SendPingAsync(ipAddress, timeoutMs);

                if (reply.Status == IPStatus.Success)
                {
                    device.Status = "Online";
                    device.LastLatencyMs = (int)reply.RoundtripTime;

                    // Пытаемся получить имя хоста
                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                        device.HostName = hostEntry.HostName;
                    }
                    catch (SocketException)
                    {
                        device.HostName = "Не удалось определить";
                    }
                }
                else
                {
                    device.Status = "Offline";
                }
            }
            catch (PingException)
            {
                device.Status = "Offline";
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка ping для {ipAddress}: {ex.Message}");
                device.Status = "Error";
            }

            return device;
        }
        finally
        {
            _throttler.Release();
        }
    }

    private async Task TcpPortScanAsync(List<string> hosts, ScanRequest request, ScanResult result)
    {
        var commonPorts = new[] { 80, 443, 22, 21, 25, 3389, 8080, 53 };
        var targetPorts = request.TargetPorts.Any() ? request.TargetPorts : commonPorts.ToList();

        foreach (var ip in hosts)
        {
            var device = new DeviceInfo
            {
                IpAddress = ip,
                Status = "Offline",
                LastLatencyMs = null,
                HostName = "",
                OpenPorts = ""
            };

            var openPorts = new List<int>();

            foreach (var port in targetPorts)
            {
                if (await CheckPortAsync(ip, port, request.TimeoutMs))
                {
                    openPorts.Add(port);
                }
            }

            if (openPorts.Any())
            {
                device.Status = "Online";
                device.OpenPorts = string.Join(", ", openPorts);

                // Пытаемся получить имя хоста
                try
                {
                    var hostEntry = await Dns.GetHostEntryAsync(ip);
                    device.HostName = hostEntry.HostName;
                }
                catch (SocketException)
                {
                    device.HostName = "";
                }
            }

            result.Devices.Add(device);
        }
    }

    private async Task<bool> CheckPortAsync(string ip, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(timeoutMs);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == connectTask)
            {
                try
                {
                    await connectTask;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            client.Close();
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task ArpScanAsync(List<string> hosts, ScanRequest request, ScanResult result)
    {
        // ARP сканирование работает только в локальной сети
        foreach (var ip in hosts)
        {
            var device = new DeviceInfo
            {
                IpAddress = ip,
                Status = "Offline",
                LastLatencyMs = null,
                HostName = "",
                OpenPorts = ""
            };

            try
            {
                // Сначала проверяем через ping
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 1000);

                if (reply.Status == IPStatus.Success)
                {
                    device.Status = "Online";
                    device.LastLatencyMs = (int)reply.RoundtripTime;

                    // Пытаемся получить MAC адрес через ARP
                    var macAddress = await GetMacAddressAsync(ip);
                    if (!string.IsNullOrEmpty(macAddress))
                    {
                        device.HostName = $"MAC: {macAddress}";
                    }
                }
            }
            catch
            {
                device.Status = "Offline";
            }

            result.Devices.Add(device);
        }
    }

    private async Task<string?> GetMacAddressAsync(string ipAddress)
    {
        try
        {
            // Для Windows используем arp.exe
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-a {ipAddress}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    // Парсим вывод ARP
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains(ipAddress))
                        {
                            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                return parts[1]; // MAC адрес
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Игнорируем ошибки
        }

        return null;
    }

    private async Task FullScanAsync(List<string> hosts, ScanRequest request, ScanResult result)
    {
        // Комбинированный метод: сначала ICMP, потом для онлайн устройств дополнительные проверки
        await IcmpScanAsync(hosts, request, result);

        // Для онлайн устройств получаем дополнительную информацию
        var onlineDevices = result.Devices.Where(d => d.Status == "Online").ToList();

        var tasks = new List<Task>();

        foreach (var device in onlineDevices)
        {
            tasks.Add(EnrichDeviceInfoAsync(device));

            if (tasks.Count >= 20)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }

        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
        }
    }

    private async Task EnrichDeviceInfoAsync(DeviceInfo device)
    {
        try
        {
            // Получаем полное имя хоста
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(device.IpAddress);
                device.HostName = hostEntry.HostName;
            }
            catch
            {
                device.HostName = "Неизвестный хост";
            }

            // Проверяем основные порты
            var commonPorts = new[] { 80, 443, 22, 3389 };
            var openPorts = new List<int>();

            foreach (var port in commonPorts)
            {
                if (await CheckPortAsync(device.IpAddress, port, 1000))
                {
                    openPorts.Add(port);
                }
            }

            if (openPorts.Any())
            {
                device.OpenPorts = string.Join(", ", openPorts);
            }
        }
        catch
        {
            // Игнорируем ошибки обогащения
        }
    }

    private async Task NetworkDiscoveryAsync(ScanRequest request, ScanResult result)
    {
        // Обнаружение сетей в системе
        try
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                             nic.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            foreach (var nic in networkInterfaces)
            {
                var ipProperties = nic.GetIPProperties();
                var unicastAddresses = ipProperties.UnicastAddresses
                    .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork);

                foreach (var addr in unicastAddresses)
                {
                    var ip = addr.Address.ToString();
                    var subnetMask = addr.IPv4Mask?.ToString();

                    if (!string.IsNullOrEmpty(subnetMask))
                    {
                        // Преобразуем в CIDR
                        var cidr = CalculateCidr(ip, subnetMask);
                        if (!string.IsNullOrEmpty(cidr))
                        {
                            // Сканируем эту сеть
                            var hosts = CidrHelper.EnumerateHosts(cidr);
                            await IcmpScanAsync(hosts.Take(50).ToList(), request, result);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка обнаружения сетей: {ex.Message}");
        }
    }

    private string? CalculateCidr(string ip, string subnetMask)
    {
        try
        {
            var maskParts = subnetMask.Split('.');
            var cidr = 0;

            foreach (var part in maskParts)
            {
                var octet = int.Parse(part);
                while (octet > 0)
                {
                    cidr += octet & 1;
                    octet >>= 1;
                }
            }

            return $"{ip}/{cidr}";
        }
        catch
        {
            return null;
        }
    }

    private async Task ScanSpecificPortsAsync(List<DeviceInfo> devices, ScanRequest request)
    {
        var onlineDevices = devices.Where(d => d.Status == "Online").ToList();

        foreach (var device in onlineDevices)
        {
            var openPorts = new List<int>();

            foreach (var port in request.TargetPorts)
            {
                if (await CheckPortAsync(device.IpAddress, port, request.TimeoutMs))
                {
                    openPorts.Add(port);
                }
            }

            if (openPorts.Any())
            {
                device.OpenPorts = string.Join(", ", openPorts);
            }
        }
    }
}
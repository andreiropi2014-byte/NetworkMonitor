using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Timers;
using System.Windows;
using NetworkMonitor.Client.Logging;
using NetworkMonitor.Client.Networking;
using NetworkMonitor.Shared.Models;

namespace NetworkMonitor.Client.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ClientConnection _client;
    private readonly ILogger _logger;
    private readonly System.Timers.Timer _monitorTimer;
    private bool _isConnected;

    public ObservableCollection<DeviceInfo> Devices { get; } = new();

    private string _serverIp = "127.0.0.1";
    public string ServerIp
    {
        get => _serverIp;
        set { _serverIp = value; OnPropertyChanged(); }
    }

    private int _serverPort = 5000;
    public int ServerPort
    {
        get => _serverPort;
        set { _serverPort = value; OnPropertyChanged(); }
    }

    private string _cidr = "192.168.1.0/24";
    public string Cidr
    {
        get => _cidr;
        set { _cidr = value; OnPropertyChanged(); }
    }

    private bool _isMonitoring;
    public bool IsMonitoring
    {
        get => _isMonitoring;
        set
        {
            if (_isMonitoring != value)
            {
                _isMonitoring = value;
                OnPropertyChanged();
            }
        }
    }

    private int _monitorIntervalSec = 5;
    public int MonitorIntervalSec
    {
        get => _monitorIntervalSec;
        set
        {
            _monitorIntervalSec = value;
            if (_monitorTimer != null)
            {
                _monitorTimer.Interval = _monitorIntervalSec * 1000;
            }
            OnPropertyChanged();
        }
    }

    private string _statusMessage = "Не подключено";
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
                StatusMessage = value ? "Подключено" : "Не подключено";
            }
        }
    }

    public MainViewModel()
    {
        Console.WriteLine("[MainViewModel] Конструктор вызван");

        _logger = new ConsoleFileLogger("logs/client.log");
        _client = new ClientConnection(_logger);

        Console.WriteLine("[MainViewModel] Подписываюсь на события...");

        // Подписка на события
        _client.ScanResultReceived += OnScanResult;
        _client.ErrorReceived += OnErrorReceived;
        _client.ConnectionStateChanged += OnConnectionStateChanged;

        Console.WriteLine("[MainViewModel] События подписаны");

        // Настройка таймера
        _monitorTimer = new System.Timers.Timer(MonitorIntervalSec * 1000);
        _monitorTimer.Elapsed += OnMonitorTimerElapsed;
        _monitorTimer.AutoReset = true;

        Console.WriteLine("[MainViewModel] Таймер настроен");
    }

    private void OnMonitorTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        Console.WriteLine($"[MainViewModel] Таймер сработал, IsConnected={IsConnected}");

        // Запускаем сканирование в отдельной задаче
        Task.Run(async () =>
        {
            if (!IsConnected)
            {
                Console.WriteLine("[MainViewModel] Нет подключения, останавливаю мониторинг");
                StopMonitoring();
                return;
            }

            await StartScanAsync();
        });
    }

    private void OnConnectionStateChanged(bool isConnected)
    {
        Console.WriteLine($"[MainViewModel] ConnectionStateChanged: {isConnected}");

        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsConnected = isConnected;
            if (!isConnected && IsMonitoring)
            {
                Console.WriteLine("[MainViewModel] Отключение при активном мониторинге, останавливаю");
                StopMonitoring();
            }
        });
    }

    private void OnErrorReceived(string errorMessage)
    {
        Console.WriteLine($"[MainViewModel] ErrorReceived: {errorMessage}");

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _logger.LogError(errorMessage);
            MessageBox.Show(errorMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

            if (IsMonitoring)
            {
                Console.WriteLine("[MainViewModel] Ошибка при мониторинге, останавливаю");
                StopMonitoring();
            }
        });
    }

    public async Task ConnectAsync()
    {
        Console.WriteLine($"[MainViewModel.ConnectAsync] Начало подключения к {ServerIp}:{ServerPort}");

        if (string.IsNullOrWhiteSpace(ServerIp))
        {
            OnErrorReceived("IP сервера не указан");
            return;
        }

        if (ServerPort <= 0 || ServerPort > 65535)
        {
            OnErrorReceived("Некорректный порт");
            return;
        }

        try
        {
            if (IsMonitoring)
            {
                Console.WriteLine("[MainViewModel] Останавливаю мониторинг перед переподключением");
                StopMonitoring();
            }

            StatusMessage = "Подключение...";
            var ok = await _client.ConnectAsync(ServerIp, ServerPort);

            if (!ok)
            {
                OnErrorReceived("Не удалось подключиться к серверу");
            }
            else
            {
                Console.WriteLine("[MainViewModel] Подключение успешно");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] Ошибка подключения: {ex.Message}");
            OnErrorReceived($"Ошибка подключения: {ex.Message}");
        }
    }

    // Добавляем метод Disconnect
    public void Disconnect()
    {
        Console.WriteLine("[MainViewModel.Disconnect] Отключение от сервера");

        try
        {
            // Останавливаем мониторинг
            StopMonitoring();

            // Отключаемся от сервера
            _client.Disconnect();

            // Очищаем список устройств
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Devices.Clear();
            });

            _logger.LogInfo("Отключено от сервера");
            Console.WriteLine("[MainViewModel] Отключено от сервера");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] Ошибка отключения: {ex.Message}");
            _logger.LogError($"Ошибка отключения: {ex.Message}");
            OnErrorReceived($"Ошибка отключения: {ex.Message}");
        }
    }

    public async Task StartScanAsync()
    {
        Console.WriteLine($"[MainViewModel.StartScanAsync] Начало сканирования CIDR={Cidr}");

        if (!IsConnected)
        {
            Console.WriteLine("[MainViewModel] Нет подключения к серверу");
            OnErrorReceived("Нет подключения к серверу");
            return;
        }

        if (string.IsNullOrWhiteSpace(Cidr))
        {
            OnErrorReceived("Не указана CIDR сеть");
            return;
        }

        try
        {
            var req = new ScanRequest
            {
                CidrNetwork = Cidr,
                TimeoutMs = 1000
            };

            Console.WriteLine($"[MainViewModel] Отправляю запрос на сканирование");
            await _client.SendScanRequestAsync(req);
            Console.WriteLine($"[MainViewModel] Запрос отправлен");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] Ошибка сканирования: {ex.Message}");
            OnErrorReceived($"Ошибка сканирования: {ex.Message}");

            if (IsMonitoring)
            {
                StopMonitoring();
            }
        }
    }

    public void StartMonitoring()
    {
        Console.WriteLine($"[MainViewModel.StartMonitoring] Запуск мониторинга");

        if (IsMonitoring || !IsConnected)
        {
            Console.WriteLine($"[MainViewModel] Мониторинг уже запущен или нет подключения");
            return;
        }

        if (string.IsNullOrWhiteSpace(Cidr))
        {
            OnErrorReceived("Не указана CIDR сеть для мониторинга");
            return;
        }

        try
        {
            _monitorTimer.Interval = MonitorIntervalSec * 1000;
            _monitorTimer.Start();
            IsMonitoring = true;
            _logger.LogInfo($"Мониторинг запущен (интервал: {MonitorIntervalSec} сек)");
            Console.WriteLine($"[MainViewModel] Мониторинг запущен, интервал: {MonitorIntervalSec} сек");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] Ошибка запуска мониторинга: {ex.Message}");
            OnErrorReceived($"Ошибка запуска мониторинга: {ex.Message}");
        }
    }

    public void StopMonitoring()
    {
        Console.WriteLine($"[MainViewModel.StopMonitoring] Остановка мониторинга");

        if (!IsMonitoring)
        {
            Console.WriteLine($"[MainViewModel] Мониторинг уже остановлен");
            return;
        }

        try
        {
            _monitorTimer.Stop();
            IsMonitoring = false;
            _logger.LogInfo("Мониторинг остановлен");
            Console.WriteLine($"[MainViewModel] Мониторинг остановлен");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] Ошибка остановки мониторинга: {ex.Message}");
            _logger.LogError($"Ошибка остановки мониторинга: {ex.Message}");
        }
    }

    public void Cleanup()
    {
        Console.WriteLine($"[MainViewModel.Cleanup] Очистка ресурсов");
        Disconnect(); // Используем метод Disconnect вместо прямой очистки
        _monitorTimer?.Dispose();
    }

    private void OnScanResult(ScanResult result)
    {
        Console.WriteLine($"[MainViewModel.OnScanResult] Получен результат!");
        Console.WriteLine($"[MainViewModel] RequestId: {result.RequestId}");
        Console.WriteLine($"[MainViewModel] Devices count: {result.Devices?.Count ?? 0}");
        Console.WriteLine($"[MainViewModel] Message: {result.Message}");

        if (result.Devices != null)
        {
            foreach (var device in result.Devices.Take(3))
            {
                Console.WriteLine($"[MainViewModel] Устройство: {device.IpAddress} - {device.Status}");
            }
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Console.WriteLine($"[MainViewModel] UI Thread - обновление данных");

            // Очищаем коллекцию
            Devices.Clear();

            if (result.Devices != null && result.Devices.Any())
            {
                Console.WriteLine($"[MainViewModel] Добавляем {result.Devices.Count} устройств");

                foreach (var device in result.Devices.OrderBy(x => x.IpAddress))
                {
                    Devices.Add(device);
                }

                var onlineCount = result.Devices.Count(d => d.Status == "Online");
                StatusMessage = $"Устройств: {result.Devices.Count}, онлайн: {onlineCount}";

                Console.WriteLine($"[MainViewModel] Обновлено! Devices.Count = {Devices.Count}");
            }
            else
            {
                Console.WriteLine($"[MainViewModel] Нет устройств в результате");
                StatusMessage = "Нет устройств в сети";
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
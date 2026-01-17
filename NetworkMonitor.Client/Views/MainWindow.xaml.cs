using System.ComponentModel;
using System.Windows;
using NetworkMonitor.Client.ViewModels;

namespace NetworkMonitor.Client.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Подписка на события для обновления UI
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.IsMonitoring))
        {
            // Обновляем видимость кнопок мониторинга
            if (_vm.IsMonitoring)
            {
                StartMonitoringBtn.Visibility = Visibility.Collapsed;
                StopMonitoringBtn.Visibility = Visibility.Visible;
            }
            else
            {
                StartMonitoringBtn.Visibility = Visibility.Visible;
                StopMonitoringBtn.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ConnectAsync();
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _vm.Disconnect();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await _vm.StartScanAsync();
    }

    private void StartMonitoring_Click(object sender, RoutedEventArgs e)
    {
        _vm.StartMonitoring();
    }

    private void StopMonitoring_Click(object sender, RoutedEventArgs e)
    {
        _vm.StopMonitoring();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _vm.Cleanup();
    }
}
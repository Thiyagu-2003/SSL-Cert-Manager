using System;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;

namespace CertManager.Desktop.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;
    private readonly DispatcherTimer _pollTimer;

    private bool _isServiceConnected;
    private string _statusMessage = "Connecting to background service...";
    private string _version = "v2.0.0";
    private string _uptime = "0s";
    private int _totalCertificates;
    private int _expiringSoonCount;
    private int _activeOrdersCount;
    private string _lastRefreshed = "Never";
    private bool _isBusy;

    public DashboardViewModel()
    {
        _ipcClient = new ServiceIpcClient();
        RefreshCommand = new RelayCommand(async () => await RefreshStatusAsync());

        // Polling timer to update connection and stats
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _pollTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _pollTimer.Start();

        _ = RefreshStatusAsync();
    }

    public bool IsServiceConnected
    {
        get => _isServiceConnected;
        set => SetProperty(ref _isServiceConnected, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    public string Uptime
    {
        get => _uptime;
        set => SetProperty(ref _uptime, value);
    }

    public int TotalCertificates
    {
        get => _totalCertificates;
        set => SetProperty(ref _totalCertificates, value);
    }

    public int ExpiringSoonCount
    {
        get => _expiringSoonCount;
        set => SetProperty(ref _expiringSoonCount, value);
    }

    public int ActiveOrdersCount
    {
        get => _activeOrdersCount;
        set => SetProperty(ref _activeOrdersCount, value);
    }

    public string LastRefreshed
    {
        get => _lastRefreshed;
        set => SetProperty(ref _lastRefreshed, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand RefreshCommand { get; }

    public async Task RefreshStatusAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var status = await _ipcClient.GetStatusAsync();
            if (status != null && status.IsRunning)
            {
                IsServiceConnected = true;
                StatusMessage = "Service Online & Healthy";
                Version = $"v{status.Version}";
                TotalCertificates = status.TotalCertificates;
                ExpiringSoonCount = status.ExpiringSoonCount;
                ActiveOrdersCount = status.ActiveOrdersCount;

                var timeSpan = TimeSpan.FromSeconds(status.UptimeSeconds);
                Uptime = timeSpan.TotalHours >= 1
                    ? $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m"
                    : $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else
            {
                IsServiceConnected = false;
                StatusMessage = "Background Service Offline (Start Windows Service)";
            }
        }
        catch (Exception ex)
        {
            IsServiceConnected = false;
            StatusMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            LastRefreshed = DateTime.Now.ToString("HH:mm:ss");
            IsBusy = false;
        }
    }
}

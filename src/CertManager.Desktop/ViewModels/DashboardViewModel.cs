using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Desktop.Views;
using CertManager.Service;
using Microsoft.Extensions.Hosting;

namespace CertManager.Desktop.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;
    private readonly DispatcherTimer _pollTimer;
    private readonly Action<string>? _navigateAction;
    private IHost? _inProcessHost;
    private bool _autoStartAttempted;

    private bool _isServiceConnected;
    private string _statusMessage = "Connecting to background service...";
    private string _version = "v2.0.0";
    private string _uptime = "0s";
    private int _totalCertificates;
    private int _expiringSoonCount;
    private int _activeOrdersCount;
    private string _lastRefreshed = "Never";
    private bool _isBusy;

    public DashboardViewModel(Action<string>? navigateAction = null)
    {
        _navigateAction = navigateAction;
        _ipcClient = new ServiceIpcClient();

        RefreshCommand = new RelayCommand(async () => await RefreshStatusAsync());
        StartEngineCommand = new RelayCommand(async () => await StartInProcessEngineAsync());
        NewCertificateWizardCommand = new RelayCommand(async () => await OpenNewCertificateWizardAsync(false));
        ScanIisServerCommand = new RelayCommand(async () => await ScanIisServerAsync());
        RunStagingPreflightCommand = new RelayCommand(async () => await OpenNewCertificateWizardAsync(true));

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
    public ICommand StartEngineCommand { get; }
    public ICommand NewCertificateWizardCommand { get; }
    public ICommand ScanIisServerCommand { get; }
    public ICommand RunStagingPreflightCommand { get; }

    public async Task StartInProcessEngineAsync()
    {
        if (_inProcessHost != null) return;
        try
        {
            StatusMessage = "Starting in-process background engine...";
            _inProcessHost = ServiceHostBuilder.BuildHost();
            await _inProcessHost.StartAsync();
            await Task.Delay(500);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start engine: {ex.Message}";
        }
    }

    public async Task OpenNewCertificateWizardAsync(bool isStaging = false)
    {
        if (!IsServiceConnected && _inProcessHost == null)
        {
            await StartInProcessEngineAsync();
        }

        var wizardVm = new CertificateWizardViewModel(1, "Default Web Site", "example.com")
        {
            IsStaging = isStaging
        };

        var window = new CertificateWizardWindow(wizardVm)
        {
            Owner = Application.Current?.MainWindow
        };

        window.ShowDialog();
        _ = RefreshStatusAsync();
    }

    public async Task ScanIisServerAsync()
    {
        if (!IsServiceConnected && _inProcessHost == null)
        {
            await StartInProcessEngineAsync();
        }

        _navigateAction?.Invoke("Websites");
    }

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
                if (!_autoStartAttempted && _inProcessHost == null)
                {
                    _autoStartAttempted = true;
                    IsBusy = false;
                    await StartInProcessEngineAsync();
                    return;
                }

                IsServiceConnected = false;
                StatusMessage = "Background Service Offline (Start Windows Service)";
            }
        }
        catch
        {
            if (!_autoStartAttempted && _inProcessHost == null)
            {
                _autoStartAttempted = true;
                IsBusy = false;
                await StartInProcessEngineAsync();
                return;
            }

            IsServiceConnected = false;
            StatusMessage = "Background Service Offline (Start Windows Service)";
        }
        finally
        {
            LastRefreshed = DateTime.Now.ToString("HH:mm:ss");
            IsBusy = false;
        }
    }
}

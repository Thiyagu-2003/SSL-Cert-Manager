using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CertManager.Core.Models;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Desktop.Views;
using CertManager.Service;

namespace CertManager.Desktop.ViewModels;

public class WebsitesViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;
    private bool _isBusy;
    private string _statusMessage = "Ready";
    private int _totalSitesCount;
    private int _securedSitesCount;
    private int _unencryptedSitesCount;

    public WebsitesViewModel()
    {
        _ipcClient = new ServiceIpcClient();
        Sites = new ObservableCollection<DiscoveredIisSite>();
        ScanCommand = new RelayCommand(async () => await ScanWebsitesAsync());
        ToggleHttpsRedirectCommand = new RelayCommand(async param =>
        {
            if (param is DiscoveredIisSite site)
            {
                bool targetState = !site.HasHttpsRedirect;
                bool success = await _ipcClient.ToggleHttpsRedirectAsync(site.SiteId, targetState);
                if (success)
                {
                    await ScanWebsitesAsync();
                }
            }
        });
        RequestCertificateCommand = new RelayCommand(param =>
        {
            if (param is DiscoveredIisSite site)
            {
                OpenCertificateWizard(site);
            }
        });

        _ = ScanWebsitesAsync();
    }

    public ObservableCollection<DiscoveredIisSite> Sites { get; }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int TotalSitesCount
    {
        get => _totalSitesCount;
        set => SetProperty(ref _totalSitesCount, value);
    }

    public int SecuredSitesCount
    {
        get => _securedSitesCount;
        set => SetProperty(ref _securedSitesCount, value);
    }

    public int UnencryptedSitesCount
    {
        get => _unencryptedSitesCount;
        set => SetProperty(ref _unencryptedSitesCount, value);
    }

    public ICommand ScanCommand { get; }
    public ICommand ToggleHttpsRedirectCommand { get; }
    public ICommand RequestCertificateCommand { get; }

    public void OpenCertificateWizard(DiscoveredIisSite site)
    {
        string defaultDomain = site.Hostnames.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) 
            ?? $"{site.SiteName.ToLowerInvariant().Replace(" ", "-")}.com";
        var wizardVm = new CertificateWizardViewModel(site.SiteId, site.SiteName, defaultDomain);
        var window = new CertificateWizardWindow(wizardVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        window.ShowDialog();

        // Refresh sites after dialog closes
        _ = ScanWebsitesAsync();
    }

    public async Task ScanWebsitesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Scanning local IIS server for sites and bindings...";

        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();
            var discovered = await _ipcClient.GetWebsitesAsync();
            Sites.Clear();

            foreach (var site in discovered)
            {
                Sites.Add(site);
            }

            TotalSitesCount = Sites.Count;
            SecuredSitesCount = Sites.Count(s => s.HasHttps);
            UnencryptedSitesCount = Sites.Count(s => s.NeedsCertificate);

            if (Sites.Count == 0)
            {
                StatusMessage = "No IIS sites detected (verify Microsoft IIS is installed on this machine).";
            }
            else
            {
                StatusMessage = $"Discovered {TotalSitesCount} site(s) on local IIS server.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Discovery error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

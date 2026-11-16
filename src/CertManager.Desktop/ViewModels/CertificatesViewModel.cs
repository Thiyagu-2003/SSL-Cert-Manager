using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Common;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Models;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Service;

namespace CertManager.Desktop.ViewModels;

public class CertificateItemViewModel : ViewModelBase
{
    public CertificateItemViewModel(CertificateRecord cert)
    {
        Record = cert;
        Subject = cert.Subject.Replace("CN=", "").Trim();
        Issuer = cert.Issuer;
        Thumbprint = cert.Thumbprint;
        NotBefore = cert.NotBefore.ToString("yyyy-MM-dd");
        NotAfter = cert.NotAfter.ToString("yyyy-MM-dd");
        RemainingDays = (int)Math.Max(0, (cert.NotAfter - DateTime.UtcNow).TotalDays);
        LifetimePercent = RenewalPolicyEvaluator.CalculateLifetimeProgressPercent(cert);
        IsExpiringSoon = RenewalPolicyEvaluator.ShouldRenew(cert, cert.Profile);
        IsExpired = DateTime.UtcNow >= cert.NotAfter;
    }

    public CertificateRecord Record { get; }
    public string Subject { get; }
    public string Issuer { get; }
    public string Thumbprint { get; }
    public string NotBefore { get; }
    public string NotAfter { get; }
    public int RemainingDays { get; }
    public int LifetimePercent { get; }
    public bool IsExpiringSoon { get; }
    public bool IsExpired { get; }
}

public class CertificatesViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;
    private bool _isBusy;
    private string _statusMessage = "Ready";
    private int _totalCertificates;
    private int _expiringSoonCount;
    private int _expiredCount;

    public CertificatesViewModel()
    {
        _ipcClient = new ServiceIpcClient();
        Certificates = new ObservableCollection<CertificateItemViewModel>();
        RefreshCommand = new RelayCommand(async () => await LoadCertificatesAsync());
        ForceRenewCommand = new RelayCommand(async param =>
        {
            if (param is CertificateItemViewModel item)
            {
                await ForceRenewCertificateAsync(item);
            }
        });

        _ = LoadCertificatesAsync();
    }

    public ObservableCollection<CertificateItemViewModel> Certificates { get; }

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

    public int ExpiredCount
    {
        get => _expiredCount;
        set => SetProperty(ref _expiredCount, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ForceRenewCommand { get; }

    public async Task LoadCertificatesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await LoadCertificatesInternalAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCertificatesInternalAsync()
    {
        StatusMessage = "Loading certificate inventory from background service...";

        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();
            var certs = await _ipcClient.GetCertificatesAsync();
            Certificates.Clear();

            foreach (var cert in certs)
            {
                Certificates.Add(new CertificateItemViewModel(cert));
            }

            TotalCertificates = Certificates.Count;
            ExpiringSoonCount = Certificates.Count(c => c.IsExpiringSoon && !c.IsExpired);
            ExpiredCount = Certificates.Count(c => c.IsExpired);

            if (Certificates.Count == 0)
            {
                StatusMessage = "No active certificates in store. Secure an IIS site to issue your first certificate.";
            }
            else
            {
                StatusMessage = $"Tracking {TotalCertificates} managed certificate(s) in Windows Certificate Store.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load certificates: {ex.Message}";
        }
    }

    private async Task ForceRenewCertificateAsync(CertificateItemViewModel item)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = $"Force renewing certificate for {item.Subject}...";

        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();

            bool isWildcard = item.Subject.StartsWith("*.", StringComparison.Ordinal);
            string cleanDomain = item.Subject.TrimStart('*', '.');
            string contactEmail = string.IsNullOrWhiteSpace(cleanDomain) ? "admin@example.com" : $"admin@{cleanDomain}";

            var request = new CertificateRequestModel
            {
                SiteId = 1,
                DomainName = item.Subject,
                ContactEmail = contactEmail,
                IsStaging = item.Issuer.Contains("Staging", StringComparison.OrdinalIgnoreCase),
                ValidationMethod = isWildcard ? "Dns01" : "Http01"
            };

            var result = await _ipcClient.RequestCertificateAsync(request);
            if (result != null && result.Success)
            {
                StatusMessage = $"Renewal succeeded for {item.Subject}! New thumbprint: {result.Thumbprint}";
                await LoadCertificatesInternalAsync();
            }
            else
            {
                StatusMessage = $"Renewal failed for {item.Subject}: {result?.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Renewal error for {item.Subject}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Models;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;

namespace CertManager.Desktop.ViewModels;

public class CertificateWizardViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;

    private long _siteId;
    private string _siteName = string.Empty;
    private string _domainName = string.Empty;
    private string _contactEmail = string.Empty;
    private bool _isStaging = true;
    private bool _isDns01;
    private string _dnsProvider = "Cloudflare";
    private string _dnsApiToken = string.Empty;
    private string _dnsZoneId = string.Empty;
    private bool _isExecuting;
    private string _currentStep = "Ready";
    private string _statusMessage = string.Empty;
    private bool _isSuccess;
    private string _issuedThumbprint = string.Empty;
    private string _issuedExpiration = string.Empty;
    private string _errorMessage = string.Empty;

    public CertificateWizardViewModel() : this(1, "Default Web Site", "example.com")
    {
    }

    public CertificateWizardViewModel(long siteId, string siteName, string defaultDomain)
    {
        _ipcClient = new ServiceIpcClient();
        _siteId = siteId;
        _siteName = siteName;
        _domainName = defaultDomain;
        _contactEmail = $"admin@{defaultDomain.TrimStart('*', '.')}";

        if (_domainName.StartsWith("*.", StringComparison.Ordinal))
        {
            _isDns01 = true;
        }

        StartIssuanceCommand = new RelayCommand(async () => await StartIssuanceAsync(), () => !IsExecuting && !string.IsNullOrWhiteSpace(DomainName) && !string.IsNullOrWhiteSpace(ContactEmail));
    }

    public long SiteId
    {
        get => _siteId;
        set => SetProperty(ref _siteId, value);
    }

    public string SiteName
    {
        get => _siteName;
        set => SetProperty(ref _siteName, value);
    }

    public string DomainName
    {
        get => _domainName;
        set
        {
            if (SetProperty(ref _domainName, value))
            {
                if (!string.IsNullOrWhiteSpace(value) && value.Contains('*'))
                {
                    IsDns01 = true;
                }
            }
        }
    }

    public string ContactEmail
    {
        get => _contactEmail;
        set => SetProperty(ref _contactEmail, value);
    }

    public bool IsStaging
    {
        get => _isStaging;
        set => SetProperty(ref _isStaging, value);
    }

    public bool IsDns01
    {
        get => _isDns01;
        set => SetProperty(ref _isDns01, value);
    }

    public bool IsHttp01
    {
        get => !_isDns01;
        set => IsDns01 = !value;
    }

    public string DnsProvider
    {
        get => _dnsProvider;
        set => SetProperty(ref _dnsProvider, value);
    }

    public string DnsApiToken
    {
        get => _dnsApiToken;
        set => SetProperty(ref _dnsApiToken, value);
    }

    public string DnsZoneId
    {
        get => _dnsZoneId;
        set => SetProperty(ref _dnsZoneId, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
    }

    public string CurrentStep
    {
        get => _currentStep;
        set => SetProperty(ref _currentStep, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        set => SetProperty(ref _isSuccess, value);
    }

    public string IssuedThumbprint
    {
        get => _issuedThumbprint;
        set => SetProperty(ref _issuedThumbprint, value);
    }

    public string IssuedExpiration
    {
        get => _issuedExpiration;
        set => SetProperty(ref _issuedExpiration, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand StartIssuanceCommand { get; }

    public async Task StartIssuanceAsync()
    {
        if (IsExecuting) return;
        IsExecuting = true;
        IsSuccess = false;
        ErrorMessage = string.Empty;
        CurrentStep = "Connecting to Let's Encrypt...";
        StatusMessage = "Dispatching certificate issuance to CertFlow background service...";

        try
        {
            var request = new CertificateRequestModel
            {
                SiteId = SiteId,
                DomainName = DomainName.Trim(),
                ContactEmail = ContactEmail.Trim(),
                IsStaging = IsStaging,
                ValidationMethod = IsDns01 ? "Dns01" : "Http01",
                DnsProvider = DnsProvider,
                DnsApiToken = DnsApiToken?.Trim(),
                DnsZoneId = DnsZoneId?.Trim()
            };

            CurrentStep = IsDns01 ? "DNS-01 ACME Challenge in Progress" : "Automated HTTP-01 Issuance in Progress";
            StatusMessage = IsDns01 
                ? "Creating TXT records via DNS provider, checking propagation, and finalizing certificate..."
                : "Provisioning HTTP-01 challenge, finalizing certificate, and binding to IIS...";

            var result = await _ipcClient.RequestCertificateAsync(request);

            if (result != null && result.Success)
            {
                IsSuccess = true;
                CurrentStep = "Successfully Completed";
                IssuedThumbprint = result.Thumbprint ?? "N/A";
                IssuedExpiration = result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm UTC") ?? "90 Days from issuance";
                StatusMessage = "Certificate installed into Windows Store and configured with SNI.";
            }
            else
            {
                ErrorMessage = result?.ErrorMessage ?? "An unexpected error occurred during issuance.";
                CurrentStep = "Issuance Failed";
                StatusMessage = "Failed step: " + (result?.Step ?? "Unknown");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            CurrentStep = "Exception";
            StatusMessage = "Failed to communicate with service.";
        }
        finally
        {
            IsExecuting = false;
        }
    }
}

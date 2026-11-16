using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Models;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Service;

namespace CertManager.Desktop.ViewModels;

public class CertificateWizardViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;

    private long _siteId;
    private string _siteName = string.Empty;
    private string _domainName = string.Empty;
    private string _contactEmail = string.Empty;
    private bool _isStaging = true;
    private bool _isSelfSigned;
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
        string cleanDomain = defaultDomain.TrimStart('*', '.');

        var userSettings = UserSettingsService.Instance.Settings;
        if (!string.IsNullOrWhiteSpace(userSettings.GlobalContactEmail))
        {
            _contactEmail = userSettings.GlobalContactEmail;
        }
        else
        {
            _contactEmail = string.IsNullOrWhiteSpace(cleanDomain) ? "admin@example.com" : $"admin@{cleanDomain}";
        }

        if (userSettings.PreferredEnvironment == "Production")
        {
            IsProduction = true;
        }
        else if (userSettings.PreferredEnvironment == "SelfSigned")
        {
            IsSelfSigned = true;
        }

        if (_domainName.StartsWith("*.", StringComparison.Ordinal))
        {
            _isDns01 = true;
        }

        StartIssuanceCommand = new RelayCommand(
            async () => await StartIssuanceAsync(),
            () => !IsExecuting && !string.IsNullOrWhiteSpace(DomainName) && !string.IsNullOrWhiteSpace(ContactEmail));
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
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ContactEmail
    {
        get => _contactEmail;
        set
        {
            if (SetProperty(ref _contactEmail, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsStaging
    {
        get => _isStaging;
        set
        {
            if (value)
            {
                _isStaging = true;
                _isSelfSigned = false;
            }
            else
            {
                _isStaging = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProduction));
            OnPropertyChanged(nameof(IsSelfSigned));
        }
    }

    public bool IsProduction
    {
        get => !_isStaging && !_isSelfSigned;
        set
        {
            if (value)
            {
                _isStaging = false;
                _isSelfSigned = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStaging));
            OnPropertyChanged(nameof(IsSelfSigned));
        }
    }

    public bool IsSelfSigned
    {
        get => _isSelfSigned;
        set
        {
            if (value)
            {
                _isSelfSigned = true;
                _isStaging = false;
            }
            else
            {
                _isSelfSigned = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStaging));
            OnPropertyChanged(nameof(IsProduction));
        }
    }

    public bool IsDns01
    {
        get => _isDns01;
        set
        {
            if (SetProperty(ref _isDns01, value))
            {
                OnPropertyChanged(nameof(IsHttp01));
            }
        }
    }

    public bool IsHttp01
    {
        get => !_isDns01;
        set
        {
            if (value)
            {
                IsDns01 = false;
            }
        }
    }

    public string DnsProvider
    {
        get => _dnsProvider;
        set
        {
            if (SetProperty(ref _dnsProvider, value))
            {
                OnPropertyChanged(nameof(IsCloudflareDns));
                OnPropertyChanged(nameof(IsManualDns));
            }
        }
    }

    public bool IsCloudflareDns
    {
        get => string.Equals(_dnsProvider, "Cloudflare", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                DnsProvider = "Cloudflare";
            }
        }
    }

    public bool IsManualDns
    {
        get => string.Equals(_dnsProvider, "Manual", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                DnsProvider = "Manual";
            }
        }
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
        set
        {
            if (SetProperty(ref _isExecuting, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
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
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand StartIssuanceCommand { get; }

    public async Task StartIssuanceAsync()
    {
        if (IsExecuting) return;

        // Persist email & environment preferences globally for subsequent requests
        if (!string.IsNullOrWhiteSpace(ContactEmail))
        {
            var userSettings = UserSettingsService.Instance.Settings;
            userSettings.GlobalContactEmail = ContactEmail.Trim();
            if (IsProduction) userSettings.PreferredEnvironment = "Production";
            else if (IsSelfSigned) userSettings.PreferredEnvironment = "SelfSigned";
            else userSettings.PreferredEnvironment = "Staging";
            UserSettingsService.Instance.SaveSettings(userSettings);
        }

        IsExecuting = true;
        IsSuccess = false;
        ErrorMessage = string.Empty;
        CurrentStep = IsSelfSigned ? "Generating Local Certificate..." : "Connecting to Let's Encrypt...";
        StatusMessage = "Dispatching certificate issuance to CertFlow background service...";

        try
        {
            if (!IsSelfSigned && IsDns01 && IsCloudflareDns && string.IsNullOrWhiteSpace(DnsApiToken))
            {
                ErrorMessage = "Cloudflare API Token is required for automated DNS-01 validation.";
                CurrentStep = "Validation Error";
                StatusMessage = "Please enter your Cloudflare API token or switch to HTTP-01.";
                return;
            }

            // Ensure service / engine is available
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();

            string validationMethod = IsSelfSigned ? "SelfSigned" : (IsDns01 ? "Dns01" : "Http01");

            var request = new CertificateRequestModel
            {
                SiteId = SiteId,
                SiteName = SiteName,
                DomainName = DomainName.Trim(),
                ContactEmail = ContactEmail.Trim(),
                IsStaging = IsStaging,
                ValidationMethod = validationMethod,
                DnsProvider = DnsProvider,
                DnsApiToken = DnsApiToken?.Trim(),
                DnsZoneId = DnsZoneId?.Trim()
            };

            if (IsSelfSigned)
            {
                CurrentStep = "Generating & Installing Self-Signed Certificate";
                StatusMessage = "Generating RSA 2048 certificate, installing into Windows Store, and binding to IIS...";
            }
            else
            {
                CurrentStep = IsDns01 ? "DNS-01 ACME Challenge in Progress" : "Automated HTTP-01 Issuance in Progress";
                StatusMessage = IsDns01 
                    ? "Creating TXT records via DNS provider, checking propagation, and finalizing certificate..."
                    : "Provisioning HTTP-01 challenge, finalizing certificate, and binding to IIS...";
            }

            var result = await _ipcClient.RequestCertificateAsync(request);

            if (result != null && result.Success)
            {
                IsSuccess = true;
                CurrentStep = "Successfully Completed";
                IssuedThumbprint = result.Thumbprint ?? "N/A";
                IssuedExpiration = result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm UTC") ?? (IsSelfSigned ? "1 Year from issuance" : "90 Days from issuance");
                StatusMessage = $"Certificate successfully installed into Windows Store and bound to IIS (Site ID: {SiteId}).";
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

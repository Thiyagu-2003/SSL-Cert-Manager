using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CertManager.Core.Models;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Desktop.Views;
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
    private bool _isCheckingReadiness;
    private DomainReadinessResult? _readinessResult;
    private string _selectedCertificateAuthority = "Let's Encrypt";
    private string _customCaDirectoryUrl = string.Empty;
    private bool _enableCaFallback = true;
    private string _selectedFallbackCa = "ZeroSSL";
    private string _selectedKeyAlgorithm = "ECDSA P-256 (ES256 - Modern & Fast)";
    private bool _autoPruneSupersededCerts = true;

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
            () => !IsExecuting && !string.IsNullOrWhiteSpace(DomainName));

        ChangeGlobalEmailCommand = new RelayCommand(() =>
        {
            var setup = new InitialSetupWindow
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            if (setup.ShowDialog() == true)
            {
                var settings = UserSettingsService.Instance.Settings;
                ContactEmail = settings.GlobalContactEmail;
                if (string.Equals(settings.PreferredEnvironment, "Production", StringComparison.OrdinalIgnoreCase))
                {
                    IsProduction = true;
                }
                else if (string.Equals(settings.PreferredEnvironment, "Staging", StringComparison.OrdinalIgnoreCase))
                {
                    IsStaging = true;
                }
                CommandManager.InvalidateRequerySuggested();
            }
        });

        TestReadinessCommand = new RelayCommand(
            async () => await RunReadinessCheckAsync(),
            () => !IsCheckingReadiness && !IsExecuting && !string.IsNullOrWhiteSpace(DomainName));
    }

    public ICommand ChangeGlobalEmailCommand { get; }
    public ICommand TestReadinessCommand { get; }

    public bool IsCheckingReadiness
    {
        get => _isCheckingReadiness;
        set => SetProperty(ref _isCheckingReadiness, value);
    }

    public DomainReadinessResult? ReadinessResult
    {
        get => _readinessResult;
        set
        {
            if (SetProperty(ref _readinessResult, value))
            {
                OnPropertyChanged(nameof(HasReadinessResult));
            }
        }
    }

    public bool HasReadinessResult => ReadinessResult != null;

    public async Task RunReadinessCheckAsync()
    {
        if (IsCheckingReadiness || string.IsNullOrWhiteSpace(DomainName)) return;
        IsCheckingReadiness = true;
        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();
            ReadinessResult = await _ipcClient.TestDomainReadinessAsync(DomainName.Trim());
        }
        catch (Exception ex)
        {
            ReadinessResult = new DomainReadinessResult
            {
                OverallReady = false,
                DomainName = DomainName,
                DnsStatusMessage = $"Diagnostic check failed: {ex.Message}",
                Recommendations = new List<string> { "Ensure the CertFlow background service is active." }
            };
        }
        finally
        {
            IsCheckingReadiness = false;
        }
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

    public ObservableCollection<string> CertificateAuthorities { get; } = new()
    {
        "Let's Encrypt",
        "ZeroSSL",
        "Google Trust Services",
        "BuyPass Go SSL",
        "Self-Signed (Local Dev)",
        "Custom ACME Directory"
    };

    public ObservableCollection<string> FallbackCertificateAuthorities { get; } = new()
    {
        "ZeroSSL",
        "Google Trust Services",
        "Let's Encrypt",
        "BuyPass Go SSL"
    };

    public ObservableCollection<string> KeyAlgorithms { get; } = new()
    {
        "ECDSA P-256 (ES256 - Modern & Fast)",
        "ECDSA P-384 (ES384 - High-Security Curve)",
        "RSA 2048 (RS256 - Standard Compatibility)",
        "RSA 4096 (RS256 - High-Security RSA)"
    };

    public string SelectedCertificateAuthority
    {
        get => _selectedCertificateAuthority;
        set
        {
            if (SetProperty(ref _selectedCertificateAuthority, value))
            {
                OnPropertyChanged(nameof(IsCustomCa));
                if (value == "Self-Signed (Local Dev)")
                {
                    IsSelfSigned = true;
                }
                else if (IsSelfSigned)
                {
                    IsSelfSigned = false;
                }
            }
        }
    }

    public bool IsCustomCa => _selectedCertificateAuthority == "Custom ACME Directory";

    public string CustomCaDirectoryUrl
    {
        get => _customCaDirectoryUrl;
        set => SetProperty(ref _customCaDirectoryUrl, value);
    }

    public bool EnableCaFallback
    {
        get => _enableCaFallback;
        set => SetProperty(ref _enableCaFallback, value);
    }

    public string SelectedFallbackCa
    {
        get => _selectedFallbackCa;
        set => SetProperty(ref _selectedFallbackCa, value);
    }

    public string SelectedKeyAlgorithm
    {
        get => _selectedKeyAlgorithm;
        set => SetProperty(ref _selectedKeyAlgorithm, value);
    }

    public bool AutoPruneSupersededCerts
    {
        get => _autoPruneSupersededCerts;
        set => SetProperty(ref _autoPruneSupersededCerts, value);
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

        if (!IsSelfSigned && string.IsNullOrWhiteSpace(ContactEmail))
        {
            var setup = new InitialSetupWindow
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            if (setup.ShowDialog() == true)
            {
                ContactEmail = UserSettingsService.Instance.Settings.GlobalContactEmail;
            }

            if (string.IsNullOrWhiteSpace(ContactEmail))
            {
                ErrorMessage = "A global administrator email is required for Let's Encrypt certificates.";
                return;
            }
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

            string mappedCa = "LetsEncrypt";
            if (SelectedCertificateAuthority.Contains("ZeroSSL")) mappedCa = "ZeroSSL";
            else if (SelectedCertificateAuthority.Contains("Google")) mappedCa = "GoogleTrustServices";
            else if (SelectedCertificateAuthority.Contains("BuyPass")) mappedCa = "BuyPass";
            else if (SelectedCertificateAuthority.Contains("Self-Signed")) mappedCa = "SelfSigned";
            else if (SelectedCertificateAuthority.Contains("Custom")) mappedCa = "Custom";

            string mappedFallbackCa = "ZeroSSL";
            if (SelectedFallbackCa.Contains("Google")) mappedFallbackCa = "GoogleTrustServices";
            else if (SelectedFallbackCa.Contains("Let's Encrypt")) mappedFallbackCa = "LetsEncrypt";
            else if (SelectedFallbackCa.Contains("BuyPass")) mappedFallbackCa = "BuyPass";

            string mappedKeyAlgo = "ES256";
            if (SelectedKeyAlgorithm.Contains("P-384") || SelectedKeyAlgorithm.Contains("ES384")) mappedKeyAlgo = "ES384";
            else if (SelectedKeyAlgorithm.Contains("4096")) mappedKeyAlgo = "RS4096";
            else if (SelectedKeyAlgorithm.Contains("2048")) mappedKeyAlgo = "RS256";
            else if (SelectedKeyAlgorithm.Contains("ES256") || SelectedKeyAlgorithm.Contains("P-256")) mappedKeyAlgo = "ES256";

            var request = new CertificateRequestModel
            {
                SiteId = SiteId,
                SiteName = SiteName,
                DomainName = DomainName.Trim(),
                ContactEmail = ContactEmail.Trim(),
                IsStaging = IsStaging,
                CertificateAuthority = mappedCa,
                CustomCaDirectoryUrl = CustomCaDirectoryUrl?.Trim(),
                EnableCaFallback = EnableCaFallback,
                FallbackCertificateAuthority = mappedFallbackCa,
                KeyAlgorithm = mappedKeyAlgo,
                AutoPruneSupersededCerts = AutoPruneSupersededCerts,
                ValidationMethod = validationMethod,
                DnsProvider = DnsProvider,
                DnsApiToken = DnsApiToken?.Trim(),
                DnsZoneId = DnsZoneId?.Trim()
            };

            if (IsSelfSigned)
            {
                CurrentStep = "Generating & Installing Self-Signed Certificate";
                StatusMessage = "Generating local certificate, installing into Windows Store, and binding to IIS...";
            }
            else
            {
                CurrentStep = IsDns01 ? "DNS-01 ACME Challenge in Progress" : $"Automated HTTP-01 Issuance via {SelectedCertificateAuthority}";
                StatusMessage = IsDns01 
                    ? $"Creating TXT records, requesting {mappedKeyAlgo} cert from {SelectedCertificateAuthority}, and finalizing..."
                    : $"Provisioning HTTP-01 challenge, requesting {mappedKeyAlgo} cert from {SelectedCertificateAuthority}, and binding to IIS...";
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

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Common;
using CertManager.Core.Entities;
using CertManager.Core.Enums;
using CertManager.Core.Interfaces;
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

        Timeline = RenewalTimelineInfo.FromCertificate(cert);
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

    public RenewalTimelineInfo Timeline { get; }
    public int TotalLifetimeDays => Timeline.TotalLifetimeDays;
    public int ElapsedDays => Timeline.ElapsedDays;
    public double ProgressPercent => Timeline.ProgressPercent;
    public string ZoneLabel => Timeline.ZoneLabel;
    public string ZoneColorHex => Timeline.ZoneColorHex;
    public string ZoneBadgeBackground => Timeline.ZoneBadgeBackground;
    public string ZoneBadgeForeground => Timeline.ZoneBadgeForeground;
    public string StatusDescription => Timeline.StatusDescription;
    public bool IsInsideAriWindow => Timeline.IsInsideAriWindow;
    public int DaysUntilAriWindow => Timeline.DaysUntilAriWindow;
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
        ExportPfxCommand = new RelayCommand(async param =>
        {
            if (param is CertificateItemViewModel item)
            {
                await ExportCertificateAsync(item, CertificateExportFormat.Pfx);
            }
        });
        ExportPemCommand = new RelayCommand(async param =>
        {
            if (param is CertificateItemViewModel item)
            {
                await ExportCertificateAsync(item, CertificateExportFormat.PemBundle);
            }
        });
        BindToRdpCommand = new RelayCommand(async param =>
        {
            if (param is CertificateItemViewModel item)
            {
                await BindToRdpAsync(item);
            }
        });
        InspectLiveTlsCommand = new RelayCommand(async param =>
        {
            if (param is CertificateItemViewModel item)
            {
                await InspectLiveTlsAsync(item);
            }
        });
        ExportVaultBackupCommand = new RelayCommand(async () => await ExportVaultBackupAsync());
        ToggleGanttTimelineCommand = new RelayCommand(() =>
        {
            IsGanttTimelineExpanded = !IsGanttTimelineExpanded;
        });

        _ = LoadCertificatesAsync();
    }

    public ObservableCollection<CertificateItemViewModel> Certificates { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ForceRenewCommand { get; }
    public ICommand ExportPfxCommand { get; }
    public ICommand ExportPemCommand { get; }
    public ICommand BindToRdpCommand { get; }
    public ICommand InspectLiveTlsCommand { get; }
    public ICommand ExportVaultBackupCommand { get; }
    public ICommand ToggleGanttTimelineCommand { get; }

    private bool _isGanttTimelineExpanded;
    public bool IsGanttTimelineExpanded
    {
        get => _isGanttTimelineExpanded;
        set => SetProperty(ref _isGanttTimelineExpanded, value);
    }

    private int _inAriWindowCount;
    public int InAriWindowCount
    {
        get => _inAriWindowCount;
        set => SetProperty(ref _inAriWindowCount, value);
    }

    private int _optimalActiveCount;
    public int OptimalActiveCount
    {
        get => _optimalActiveCount;
        set => SetProperty(ref _optimalActiveCount, value);
    }

    private int _inWarningCount;
    public int InWarningCount
    {
        get => _inWarningCount;
        set => SetProperty(ref _inWarningCount, value);
    }

    private int _inCriticalCount;
    public int InCriticalCount
    {
        get => _inCriticalCount;
        set => SetProperty(ref _inCriticalCount, value);
    }

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
            InAriWindowCount = Certificates.Count(c => c.IsInsideAriWindow);
            OptimalActiveCount = Certificates.Count(c => c.Timeline.CurrentZone == RenewalTimelineZone.Active);
            InWarningCount = Certificates.Count(c => c.Timeline.CurrentZone == RenewalTimelineZone.Warning);
            InCriticalCount = Certificates.Count(c => c.Timeline.CurrentZone == RenewalTimelineZone.Critical);

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

    private async Task ExportCertificateAsync(CertificateItemViewModel item, CertificateExportFormat format)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = $"Exporting certificate {item.Subject} as {format}...";

        try
        {
            string exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CertFlow_Exports");
            Directory.CreateDirectory(exportDir);

            var req = new CertificateExportRequest(
                item.Thumbprint,
                exportDir,
                format,
                Password: null,
                DomainName: item.Subject);

            var result = await _ipcClient.ExportCertificateAsync(req);
            if (result != null && result.Success)
            {
                StatusMessage = $"Certificate successfully exported to: {result.ExportedPath}";
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exportDir,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            else
            {
                StatusMessage = $"Export failed: {result?.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BindToRdpAsync(CertificateItemViewModel item)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = $"Binding certificate {item.Subject} to Windows Remote Desktop (RDP)...";

        try
        {
            var binder = new CertManager.Infrastructure.Rds.RdsCertificateBinder();
            var res = await binder.BindCertificateToRdsAsync(item.Thumbprint);
            StatusMessage = res.Success ? res.Message : $"RDP Binding failed: {res.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"RDP binding error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InspectLiveTlsAsync(CertificateItemViewModel item)
    {
        if (IsBusy) return;
        IsBusy = true;
        string domain = item.Subject.TrimStart('*', '.');
        StatusMessage = $"Probing live TLS handshake for {domain}:443...";

        try
        {
            var inspector = new CertManager.Infrastructure.Diagnostics.LiveTlsInspector();
            var res = await inspector.InspectLiveDomainAsync(domain);
            StatusMessage = $"[{res.OverallGrade}] {domain} - {res.NegotiatedProtocol} ({res.CipherSuite}). Trust Chain: {res.ChainStatus}. HSTS: {(res.HstsHeaderPresent ? "Active" : "None")}. Remaining: {res.DaysRemaining}d.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Live TLS inspection error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportVaultBackupAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Creating encrypted CertFlow vault backup archive...";

        try
        {
            string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CertFlow", "Backups");
            Directory.CreateDirectory(backupDir);
            string backupPath = Path.Combine(backupDir, $"CertFlow_Vault_Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.vault");

            var manager = new CertManager.Infrastructure.Vault.VaultBackupManager();
            string payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                CertificatesCount = TotalCertificates,
                ExportedAtUtc = DateTime.UtcNow,
                Machine = Environment.MachineName
            });

            bool success = await manager.ExportVaultToFileAsync(backupPath, "CertFlow-Master-Passphrase-2026", payload);
            if (success)
            {
                StatusMessage = $"Encrypted vault backup created at: {backupPath}";
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = backupDir,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Vault backup error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

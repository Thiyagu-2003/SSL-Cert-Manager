using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;

namespace CertManager.Desktop.ViewModels;

public class AcmeAccountItemViewModel
{
    public string DirectoryName { get; set; } = string.Empty;
    public string DirectoryUrl { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string Status { get; set; } = "Valid / Active";
    public string EnvironmentBadgeColor { get; set; } = "#3B82F6";
    public string EnvironmentBadgeBackground { get; set; } = "#EFF6FF";
    public string TermsOfService { get; set; } = "Accepted";
}

public class AcmeAccountsViewModel : ViewModelBase
{
    private bool _isLoading;

    public AcmeAccountsViewModel()
    {
        RefreshCommand = new RelayCommand(async () => await LoadAccountsAsync());
        _ = LoadAccountsAsync();
    }

    public ObservableCollection<AcmeAccountItemViewModel> Accounts { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand RefreshCommand { get; }

    public Task LoadAccountsAsync()
    {
        Accounts.Clear();

        var globalSettings = UserSettingsService.Instance.Settings;
        string contact = string.IsNullOrWhiteSpace(globalSettings.GlobalContactEmail)
            ? "Not configured (will register on first request)"
            : globalSettings.GlobalContactEmail;

        // Let's Encrypt Production Account
        Accounts.Add(new AcmeAccountItemViewModel
        {
            DirectoryName = "Let's Encrypt Production (RFC 8555)",
            DirectoryUrl = "https://acme-v02.api.letsencrypt.org/directory",
            ContactEmail = contact,
            Status = "Active & Ready",
            EnvironmentBadgeColor = "#16A34A",
            EnvironmentBadgeBackground = "#F0FDF4",
            TermsOfService = "Accepted (v2)"
        });

        // Let's Encrypt Staging Account
        Accounts.Add(new AcmeAccountItemViewModel
        {
            DirectoryName = "Let's Encrypt Staging V2 (Testing & Sandboxes)",
            DirectoryUrl = "https://acme-staging-v02.api.letsencrypt.org/directory",
            ContactEmail = contact,
            Status = "Active & Ready (No Rate Limits)",
            EnvironmentBadgeColor = "#0284C7",
            EnvironmentBadgeBackground = "#E0F2FE",
            TermsOfService = "Accepted (v2)"
        });

        return Task.CompletedTask;
    }
}

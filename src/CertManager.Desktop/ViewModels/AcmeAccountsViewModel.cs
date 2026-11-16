using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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
    private string _globalEmailInput = string.Empty;
    private string _statusFeedback = string.Empty;
    private string _errorMessage = string.Empty;

    public AcmeAccountsViewModel()
    {
        RefreshCommand = new RelayCommand(async () => await LoadAccountsAsync());
        SaveGlobalEmailCommand = new RelayCommand(SaveGlobalEmail);

        _globalEmailInput = UserSettingsService.Instance.Settings.GlobalContactEmail;
        _ = LoadAccountsAsync();
    }

    public ObservableCollection<AcmeAccountItemViewModel> Accounts { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string GlobalEmailInput
    {
        get => _globalEmailInput;
        set
        {
            if (SetProperty(ref _globalEmailInput, value))
            {
                ErrorMessage = string.Empty;
                StatusFeedback = string.Empty;
            }
        }
    }

    public string StatusFeedback
    {
        get => _statusFeedback;
        set => SetProperty(ref _statusFeedback, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SaveGlobalEmailCommand { get; }

    public Task LoadAccountsAsync()
    {
        Accounts.Clear();

        var globalSettings = UserSettingsService.Instance.Settings;
        string contact = string.IsNullOrWhiteSpace(globalSettings.GlobalContactEmail)
            ? "Not configured (will register on first request)"
            : globalSettings.GlobalContactEmail;

        GlobalEmailInput = globalSettings.GlobalContactEmail;

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

    private void SaveGlobalEmail()
    {
        string email = (GlobalEmailInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            ErrorMessage = "Please enter a valid administrator email address.";
            StatusFeedback = string.Empty;
            return;
        }

        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ErrorMessage = "Please enter a valid email format (e.g. admin@yourdomain.com).";
            StatusFeedback = string.Empty;
            return;
        }

        var settings = UserSettingsService.Instance.Settings;
        settings.GlobalContactEmail = email;
        settings.IsInitialSetupCompleted = true;
        UserSettingsService.Instance.SaveSettings(settings);

        ErrorMessage = string.Empty;
        StatusFeedback = "Global contact email updated successfully.";
        _ = LoadAccountsAsync();
    }
}

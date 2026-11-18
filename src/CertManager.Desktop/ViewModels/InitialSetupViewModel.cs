using System;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;

namespace CertManager.Desktop.ViewModels;

public class InitialSetupViewModel : ViewModelBase
{
    private string _contactEmail = string.Empty;
    private string _webhookUrl = string.Empty;
    private bool _isProduction;
    private bool _isStaging = true;
    private string _errorMessage = string.Empty;

    public InitialSetupViewModel()
    {
        var current = UserSettingsService.Instance.Settings;
        _contactEmail = current.GlobalContactEmail;
        _webhookUrl = current.WebhookUrl;
        if (string.Equals(current.PreferredEnvironment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            _isProduction = true;
            _isStaging = false;
        }
        else
        {
            _isStaging = true;
            _isProduction = false;
        }

        SaveCommand = new RelayCommand(Save);
    }

    public string ContactEmail
    {
        get => _contactEmail;
        set
        {
            if (SetProperty(ref _contactEmail, value))
            {
                ErrorMessage = string.Empty;
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string WebhookUrl
    {
        get => _webhookUrl;
        set => SetProperty(ref _webhookUrl, value);
    }

    public bool IsStaging
    {
        get => _isStaging;
        set
        {
            if (value && !_isStaging)
            {
                _isStaging = true;
                _isProduction = false;
                OnPropertyChanged(nameof(IsStaging));
                OnPropertyChanged(nameof(IsProduction));
            }
        }
    }

    public bool IsProduction
    {
        get => _isProduction;
        set
        {
            if (value && !_isProduction)
            {
                _isProduction = true;
                _isStaging = false;
                OnPropertyChanged(nameof(IsProduction));
                OnPropertyChanged(nameof(IsStaging));
            }
        }
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

    public ICommand SaveCommand { get; }

    public event Action? RequestClose;

    public bool DialogResult { get; private set; }

    private void Save()
    {
        string email = (ContactEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            ErrorMessage = "Please enter an email address for Let's Encrypt renewal notices.";
            return;
        }

        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ErrorMessage = "Please enter a valid email address (e.g. admin@yourdomain.com).";
            return;
        }

        var settings = UserSettingsService.Instance.Settings;
        settings.GlobalContactEmail = email;
        settings.WebhookUrl = (WebhookUrl ?? string.Empty).Trim();
        settings.PreferredEnvironment = IsProduction ? "Production" : "Staging";
        settings.IsInitialSetupCompleted = true;
        UserSettingsService.Instance.SaveSettings(settings);

        DialogResult = true;
        RequestClose?.Invoke();
    }
}

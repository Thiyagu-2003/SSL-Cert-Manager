using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CertManager.Core.Models;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;
using CertManager.Infrastructure.Notifications;
using CertManager.Service;

namespace CertManager.Desktop.ViewModels;

public class NotificationsViewModel : ViewModelBase
{
    private readonly ServiceIpcClient _ipcClient;
    private bool _isLoading;
    private bool _isSaving;
    private bool _isTesting;
    private string _testingChannel = string.Empty;
    private string _testStatusMessage = string.Empty;
    private bool _testStatusIsError;
    private string _toastMessage = string.Empty;
    private bool _hasToastMessage;

    // Subscriptions
    private bool _notifyOnExpiryWarning = true;
    private int _expiryWarningDays = 14;
    private bool _notifyOnRenewalSuccess = true;
    private bool _notifyOnRenewalFailure = true;
    private bool _notifyOnManualIssuance = true;

    // Discord
    private bool _discordEnabled;
    private string _discordWebhookUrl = string.Empty;

    // Slack
    private bool _slackEnabled;
    private string _slackWebhookUrl = string.Empty;

    // Teams
    private bool _teamsEnabled;
    private string _teamsWebhookUrl = string.Empty;

    // Generic Webhook
    private bool _genericWebhookEnabled;
    private string _genericWebhookUrl = string.Empty;

    // Telegram
    private bool _telegramEnabled;
    private string _telegramBotToken = string.Empty;
    private string _telegramChatId = string.Empty;

    // SMTP
    private bool _smtpEnabled;
    private string _smtpHost = "localhost";
    private int _smtpPort = 25;
    private bool _smtpEnableSsl = true;
    private string _smtpUsername = string.Empty;
    private string _smtpPassword = string.Empty;
    private string _smtpFromAddress = "certflow@local";
    private string _smtpToAddress = string.Empty;

    // Pushover
    private bool _pushoverEnabled;
    private string _pushoverAppToken = string.Empty;
    private string _pushoverUserKey = string.Empty;

    // Gotify
    private bool _gotifyEnabled;
    private string _gotifyServerUrl = string.Empty;
    private string _gotifyAppToken = string.Empty;

    public NotificationsViewModel()
    {
        _ipcClient = new ServiceIpcClient();

        SaveSettingsCommand = new RelayCommand(async () => await SaveSettingsAsync());
        RefreshCommand = new RelayCommand(async () => await LoadSettingsAsync());

        TestDiscordCommand = new RelayCommand(async () => await TestChannelAsync("Discord", DiscordWebhookUrl));
        TestSlackCommand = new RelayCommand(async () => await TestChannelAsync("Slack", SlackWebhookUrl));
        TestTeamsCommand = new RelayCommand(async () => await TestChannelAsync("Teams", TeamsWebhookUrl));
        TestGenericWebhookCommand = new RelayCommand(async () => await TestChannelAsync("GenericWebhook", GenericWebhookUrl));
        TestTelegramCommand = new RelayCommand(async () => await TestChannelAsync("Telegram", null, TelegramBotToken, TelegramChatId));
        TestSmtpCommand = new RelayCommand(async () => await TestSmtpAsync());
        TestPushoverCommand = new RelayCommand(async () => await TestChannelAsync("Pushover", null, PushoverAppToken, PushoverUserKey));
        TestGotifyCommand = new RelayCommand(async () => await TestChannelAsync("Gotify", GotifyServerUrl, GotifyAppToken));

        _ = LoadSettingsAsync();
    }

    public ObservableCollection<int> AvailableExpiryDays { get; } = new() { 30, 14, 7, 3, 1 };

    public ICommand SaveSettingsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand TestDiscordCommand { get; }
    public ICommand TestSlackCommand { get; }
    public ICommand TestTeamsCommand { get; }
    public ICommand TestGenericWebhookCommand { get; }
    public ICommand TestTelegramCommand { get; }
    public ICommand TestSmtpCommand { get; }
    public ICommand TestPushoverCommand { get; }
    public ICommand TestGotifyCommand { get; }

    #region Properties

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

    public string TestingChannel
    {
        get => _testingChannel;
        set => SetProperty(ref _testingChannel, value);
    }

    public string TestStatusMessage
    {
        get => _testStatusMessage;
        set
        {
            if (SetProperty(ref _testStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasTestStatus));
            }
        }
    }

    public bool HasTestStatus => !string.IsNullOrWhiteSpace(TestStatusMessage);

    public bool TestStatusIsError
    {
        get => _testStatusIsError;
        set => SetProperty(ref _testStatusIsError, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        set => SetProperty(ref _toastMessage, value);
    }

    public bool HasToastMessage
    {
        get => _hasToastMessage;
        set => SetProperty(ref _hasToastMessage, value);
    }

    // Subscriptions
    public bool NotifyOnExpiryWarning
    {
        get => _notifyOnExpiryWarning;
        set => SetProperty(ref _notifyOnExpiryWarning, value);
    }

    public int ExpiryWarningDays
    {
        get => _expiryWarningDays;
        set => SetProperty(ref _expiryWarningDays, value);
    }

    public bool NotifyOnRenewalSuccess
    {
        get => _notifyOnRenewalSuccess;
        set => SetProperty(ref _notifyOnRenewalSuccess, value);
    }

    public bool NotifyOnRenewalFailure
    {
        get => _notifyOnRenewalFailure;
        set => SetProperty(ref _notifyOnRenewalFailure, value);
    }

    public bool NotifyOnManualIssuance
    {
        get => _notifyOnManualIssuance;
        set => SetProperty(ref _notifyOnManualIssuance, value);
    }

    // Discord
    public bool DiscordEnabled
    {
        get => _discordEnabled;
        set => SetProperty(ref _discordEnabled, value);
    }

    public string DiscordWebhookUrl
    {
        get => _discordWebhookUrl;
        set => SetProperty(ref _discordWebhookUrl, value);
    }

    // Slack
    public bool SlackEnabled
    {
        get => _slackEnabled;
        set => SetProperty(ref _slackEnabled, value);
    }

    public string SlackWebhookUrl
    {
        get => _slackWebhookUrl;
        set => SetProperty(ref _slackWebhookUrl, value);
    }

    // Teams
    public bool TeamsEnabled
    {
        get => _teamsEnabled;
        set => SetProperty(ref _teamsEnabled, value);
    }

    public string TeamsWebhookUrl
    {
        get => _teamsWebhookUrl;
        set => SetProperty(ref _teamsWebhookUrl, value);
    }

    // Generic Webhook
    public bool GenericWebhookEnabled
    {
        get => _genericWebhookEnabled;
        set => SetProperty(ref _genericWebhookEnabled, value);
    }

    public string GenericWebhookUrl
    {
        get => _genericWebhookUrl;
        set => SetProperty(ref _genericWebhookUrl, value);
    }

    // Telegram
    public bool TelegramEnabled
    {
        get => _telegramEnabled;
        set => SetProperty(ref _telegramEnabled, value);
    }

    public string TelegramBotToken
    {
        get => _telegramBotToken;
        set => SetProperty(ref _telegramBotToken, value);
    }

    public string TelegramChatId
    {
        get => _telegramChatId;
        set => SetProperty(ref _telegramChatId, value);
    }

    // SMTP
    public bool SmtpEnabled
    {
        get => _smtpEnabled;
        set => SetProperty(ref _smtpEnabled, value);
    }

    public string SmtpHost
    {
        get => _smtpHost;
        set => SetProperty(ref _smtpHost, value);
    }

    public int SmtpPort
    {
        get => _smtpPort;
        set => SetProperty(ref _smtpPort, value);
    }

    public bool SmtpEnableSsl
    {
        get => _smtpEnableSsl;
        set => SetProperty(ref _smtpEnableSsl, value);
    }

    public string SmtpUsername
    {
        get => _smtpUsername;
        set => SetProperty(ref _smtpUsername, value);
    }

    public string SmtpPassword
    {
        get => _smtpPassword;
        set => SetProperty(ref _smtpPassword, value);
    }

    public string SmtpFromAddress
    {
        get => _smtpFromAddress;
        set => SetProperty(ref _smtpFromAddress, value);
    }

    public string SmtpToAddress
    {
        get => _smtpToAddress;
        set => SetProperty(ref _smtpToAddress, value);
    }

    // Pushover
    public bool PushoverEnabled
    {
        get => _pushoverEnabled;
        set => SetProperty(ref _pushoverEnabled, value);
    }

    public string PushoverAppToken
    {
        get => _pushoverAppToken;
        set => SetProperty(ref _pushoverAppToken, value);
    }

    public string PushoverUserKey
    {
        get => _pushoverUserKey;
        set => SetProperty(ref _pushoverUserKey, value);
    }

    // Gotify
    public bool GotifyEnabled
    {
        get => _gotifyEnabled;
        set => SetProperty(ref _gotifyEnabled, value);
    }

    public string GotifyServerUrl
    {
        get => _gotifyServerUrl;
        set => SetProperty(ref _gotifyServerUrl, value);
    }

    public string GotifyAppToken
    {
        get => _gotifyAppToken;
        set => SetProperty(ref _gotifyAppToken, value);
    }

    #endregion

    public async Task LoadSettingsAsync()
    {
        IsLoading = true;
        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();
            var settings = await _ipcClient.GetNotificationSettingsAsync() 
                ?? NotificationSettingsManager.LoadSettings();

            ApplySettings(settings);
        }
        catch (Exception ex)
        {
            ShowToast($"Failed to load settings: {ex.Message}", isError: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SaveSettingsAsync()
    {
        if (IsSaving) return;
        IsSaving = true;

        try
        {
            var settings = ToSettings();
            NotificationSettingsManager.SaveSettings(settings);

            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();
            await _ipcClient.SaveNotificationSettingsAsync(settings);

            ShowToast("Notification settings saved successfully.");
        }
        catch (Exception ex)
        {
            ShowToast($"Error saving settings: {ex.Message}", isError: true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task TestChannelAsync(string channelType, string? targetUrl = null, string? token = null, string? secondary = null)
    {
        if (IsTesting) return;
        IsTesting = true;
        TestingChannel = channelType;
        TestStatusMessage = $"Sending test notification to {channelType}...";
        TestStatusIsError = false;

        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();

            var req = new NotificationTestRequest
            {
                ChannelType = channelType,
                TargetUrl = targetUrl,
                Token = token,
                SecondaryTokenOrChatId = secondary
            };

            var res = await _ipcClient.SendTestNotificationAsync(req);
            TestStatusMessage = res.Message;
            TestStatusIsError = !res.Success;
        }
        catch (Exception ex)
        {
            TestStatusMessage = $"Test failed: {ex.Message}";
            TestStatusIsError = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    public async Task TestSmtpAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        TestingChannel = "SMTP";
        TestStatusMessage = "Sending test email notification...";
        TestStatusIsError = false;

        try
        {
            await ServiceHostBuilder.EnsureInProcessEngineRunningAsync();

            var req = new NotificationTestRequest
            {
                ChannelType = "Smtp",
                SmtpHost = SmtpHost,
                SmtpPort = SmtpPort,
                SmtpSsl = SmtpEnableSsl,
                SmtpUser = SmtpUsername,
                SmtpPass = SmtpPassword,
                SmtpFrom = SmtpFromAddress,
                SmtpTo = SmtpToAddress
            };

            var res = await _ipcClient.SendTestNotificationAsync(req);
            TestStatusMessage = res.Message;
            TestStatusIsError = !res.Success;
        }
        catch (Exception ex)
        {
            TestStatusMessage = $"Test email failed: {ex.Message}";
            TestStatusIsError = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void ApplySettings(NotificationSettings s)
    {
        NotifyOnExpiryWarning = s.NotifyOnExpiryWarning;
        ExpiryWarningDays = s.ExpiryWarningDays > 0 ? s.ExpiryWarningDays : 14;
        NotifyOnRenewalSuccess = s.NotifyOnRenewalSuccess;
        NotifyOnRenewalFailure = s.NotifyOnRenewalFailure;
        NotifyOnManualIssuance = s.NotifyOnManualIssuance;

        DiscordEnabled = s.DiscordEnabled;
        DiscordWebhookUrl = s.DiscordWebhookUrl;

        SlackEnabled = s.SlackEnabled;
        SlackWebhookUrl = s.SlackWebhookUrl;

        TeamsEnabled = s.TeamsEnabled;
        TeamsWebhookUrl = s.TeamsWebhookUrl;

        GenericWebhookEnabled = s.GenericWebhookEnabled;
        GenericWebhookUrl = s.GenericWebhookUrl;

        TelegramEnabled = s.TelegramEnabled;
        TelegramBotToken = s.TelegramBotToken;
        TelegramChatId = s.TelegramChatId;

        SmtpEnabled = s.SmtpEnabled;
        SmtpHost = string.IsNullOrWhiteSpace(s.SmtpHost) ? "localhost" : s.SmtpHost;
        SmtpPort = s.SmtpPort > 0 ? s.SmtpPort : 25;
        SmtpEnableSsl = s.SmtpEnableSsl;
        SmtpUsername = s.SmtpUsername;
        SmtpPassword = s.SmtpPassword;
        SmtpFromAddress = string.IsNullOrWhiteSpace(s.SmtpFromAddress) ? "certflow@local" : s.SmtpFromAddress;
        SmtpToAddress = s.SmtpToAddress;

        PushoverEnabled = s.PushoverEnabled;
        PushoverAppToken = s.PushoverAppToken;
        PushoverUserKey = s.PushoverUserKey;

        GotifyEnabled = s.GotifyEnabled;
        GotifyServerUrl = s.GotifyServerUrl;
        GotifyAppToken = s.GotifyAppToken;
    }

    private NotificationSettings ToSettings()
    {
        return new NotificationSettings
        {
            NotifyOnExpiryWarning = NotifyOnExpiryWarning,
            ExpiryWarningDays = ExpiryWarningDays,
            NotifyOnRenewalSuccess = NotifyOnRenewalSuccess,
            NotifyOnRenewalFailure = NotifyOnRenewalFailure,
            NotifyOnManualIssuance = NotifyOnManualIssuance,

            DiscordEnabled = DiscordEnabled,
            DiscordWebhookUrl = DiscordWebhookUrl?.Trim() ?? string.Empty,

            SlackEnabled = SlackEnabled,
            SlackWebhookUrl = SlackWebhookUrl?.Trim() ?? string.Empty,

            TeamsEnabled = TeamsEnabled,
            TeamsWebhookUrl = TeamsWebhookUrl?.Trim() ?? string.Empty,

            GenericWebhookEnabled = GenericWebhookEnabled,
            GenericWebhookUrl = GenericWebhookUrl?.Trim() ?? string.Empty,

            TelegramEnabled = TelegramEnabled,
            TelegramBotToken = TelegramBotToken?.Trim() ?? string.Empty,
            TelegramChatId = TelegramChatId?.Trim() ?? string.Empty,

            SmtpEnabled = SmtpEnabled,
            SmtpHost = SmtpHost?.Trim() ?? "localhost",
            SmtpPort = SmtpPort,
            SmtpEnableSsl = SmtpEnableSsl,
            SmtpUsername = SmtpUsername?.Trim() ?? string.Empty,
            SmtpPassword = SmtpPassword?.Trim() ?? string.Empty,
            SmtpFromAddress = SmtpFromAddress?.Trim() ?? "certflow@local",
            SmtpToAddress = SmtpToAddress?.Trim() ?? string.Empty,

            PushoverEnabled = PushoverEnabled,
            PushoverAppToken = PushoverAppToken?.Trim() ?? string.Empty,
            PushoverUserKey = PushoverUserKey?.Trim() ?? string.Empty,

            GotifyEnabled = GotifyEnabled,
            GotifyServerUrl = GotifyServerUrl?.Trim() ?? string.Empty,
            GotifyAppToken = GotifyAppToken?.Trim() ?? string.Empty
        };
    }

    private void ShowToast(string message, bool isError = false)
    {
        ToastMessage = message;
        HasToastMessage = true;

        Task.Delay(4000).ContinueWith(_ =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                HasToastMessage = false;
            });
        });
    }
}

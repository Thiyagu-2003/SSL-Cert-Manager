using System;
using System.Windows.Input;
using CertManager.Desktop.Common;
using CertManager.Desktop.Services;

namespace CertManager.Desktop.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private object _currentView;
    private string _activeSection = "Dashboard";
    private bool _isDisposed;

    public MainViewModel()
    {
        DashboardVM = new DashboardViewModel(section => NavigateCommand?.Execute(section));
        WebsitesVM = new WebsitesViewModel();
        CertificatesVM = new CertificatesViewModel();
        ProfilesVM = new ProfilesViewModel();
        AcmeAccountsVM = new AcmeAccountsViewModel();
        NotificationsVM = new NotificationsViewModel();
        TutorialsVM = new TutorialsViewModel(section => NavigateCommand?.Execute(section));
        LogsVM = new LogsViewModel((section, guideKey) =>
        {
            if (string.Equals(section, "Tutorials", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(guideKey))
            {
                TutorialsVM.SelectedGuideKey = guideKey;
            }
            NavigateCommand?.Execute(section);
        });

        _currentView = DashboardVM;

        ToggleThemeCommand = new RelayCommand(() =>
        {
            ThemeManager.Instance.ToggleTheme();
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeButtonLabel));
        });

        ThemeManager.Instance.ThemeChanged += isDark =>
        {
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeButtonLabel));
        };

        NavigateCommand = new RelayCommand(param =>
        {
            if (param is string section)
            {
                ActiveSection = section;
                switch (section)
                {
                    case "Dashboard":
                        CurrentView = DashboardVM;
                        _ = DashboardVM.RefreshStatusAsync();
                        break;
                    case "Websites":
                        CurrentView = WebsitesVM;
                        _ = WebsitesVM.ScanWebsitesAsync();
                        break;
                    case "Certificates":
                        CurrentView = CertificatesVM;
                        _ = CertificatesVM.LoadCertificatesAsync();
                        break;
                    case "Profiles":
                        CurrentView = ProfilesVM;
                        _ = ProfilesVM.LoadProfilesAsync();
                        break;
                    case "AcmeAccounts":
                        CurrentView = AcmeAccountsVM;
                        _ = AcmeAccountsVM.LoadAccountsAsync();
                        break;
                    case "Notifications":
                        CurrentView = NotificationsVM;
                        _ = NotificationsVM.LoadSettingsAsync();
                        break;
                    case "Logs":
                        CurrentView = LogsVM;
                        _ = LogsVM.LoadLogsAsync();
                        break;
                    case "Tutorials":
                        CurrentView = TutorialsVM;
                        break;
                    default:
                        CurrentView = DashboardVM;
                        break;
                }
            }
        });
    }

    public DashboardViewModel DashboardVM { get; }
    public WebsitesViewModel WebsitesVM { get; }
    public CertificatesViewModel CertificatesVM { get; }
    public ProfilesViewModel ProfilesVM { get; }
    public AcmeAccountsViewModel AcmeAccountsVM { get; }
    public NotificationsViewModel NotificationsVM { get; }
    public LogsViewModel LogsVM { get; }
    public TutorialsViewModel TutorialsVM { get; }

    public object CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public string ActiveSection
    {
        get => _activeSection;
        set => SetProperty(ref _activeSection, value);
    }

    public ICommand NavigateCommand { get; }
    public ICommand ToggleThemeCommand { get; }

    public bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;
    public string ThemeButtonLabel => IsDarkTheme ? "Light Mode" : "Dark Mode";

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        DashboardVM.Dispose();
        LogsVM.Dispose();
    }
}

using System.Windows.Input;
using CertManager.Desktop.Common;

namespace CertManager.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    private object _currentView;
    private string _activeSection = "Dashboard";

    public MainViewModel()
    {
        DashboardVM = new DashboardViewModel();
        WebsitesVM = new WebsitesViewModel();
        CertificatesVM = new CertificatesViewModel();
        LogsVM = new LogsViewModel();

        _currentView = DashboardVM;

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
                    case "Logs":
                        CurrentView = LogsVM;
                        _ = LogsVM.LoadLogsAsync();
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
    public LogsViewModel LogsVM { get; }

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
}

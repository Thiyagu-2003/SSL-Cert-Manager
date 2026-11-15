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
                        break;
                    default:
                        CurrentView = DashboardVM;
                        break;
                }
            }
        });
    }

    public DashboardViewModel DashboardVM { get; }

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

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CertManager.Desktop.Common;
using CertManager.Desktop.Views;

namespace CertManager.Desktop.ViewModels;

public class TutorialsViewModel : ViewModelBase
{
    private readonly Action<string>? _navigateAction;
    private string _selectedGuideKey = "PublicDomain";
    private string _copyNotification = string.Empty;

    public TutorialsViewModel(Action<string>? navigateAction = null)
    {
        _navigateAction = navigateAction;

        SelectGuideCommand = new RelayCommand(param =>
        {
            if (param is string guideKey)
            {
                SelectedGuideKey = guideKey;
            }
        });

        NavigateToWebsitesCommand = new RelayCommand(() =>
        {
            _navigateAction?.Invoke("Websites");
        });

        LaunchWizardCommand = new RelayCommand(param =>
        {
            bool selfSigned = param is string s && string.Equals(s, "SelfSigned", StringComparison.OrdinalIgnoreCase);
            var wizardVm = new CertificateWizardViewModel(1, "Default Web Site", "example.com")
            {
                IsStaging = false,
                IsSelfSigned = selfSigned
            };

            var window = new CertificateWizardWindow(wizardVm)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            window.ShowDialog();
        });

        CopyServiceInstallCommand = new RelayCommand(async () =>
        {
            try
            {
                Clipboard.SetText(".\\CertFlow.exe --install-service");
                CopyNotification = "Copied to clipboard: .\\CertFlow.exe --install-service";
                await Task.Delay(3000);
                CopyNotification = string.Empty;
            }
            catch
            {
                CopyNotification = "Command: .\\CertFlow.exe --install-service";
            }
        });
    }

    public string SelectedGuideKey
    {
        get => _selectedGuideKey;
        set => SetProperty(ref _selectedGuideKey, value);
    }

    public string CopyNotification
    {
        get => _copyNotification;
        set => SetProperty(ref _copyNotification, value);
    }

    public ICommand SelectGuideCommand { get; }
    public ICommand NavigateToWebsitesCommand { get; }
    public ICommand LaunchWizardCommand { get; }
    public ICommand CopyServiceInstallCommand { get; }
}

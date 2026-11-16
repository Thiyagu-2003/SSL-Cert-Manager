using System;
using System.Windows;
using CertManager.Desktop.ViewModels;

namespace CertManager.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        Loaded += (_, _) =>
        {
            var settings = Services.UserSettingsService.Instance.Settings;
            if (string.IsNullOrWhiteSpace(settings.GlobalContactEmail) && !settings.IsInitialSetupCompleted)
            {
                var setup = new Views.InitialSetupWindow
                {
                    Owner = this
                };
                setup.ShowDialog();
            }
        };

        Closed += (_, _) =>
        {
            if (DataContext is IDisposable d)
            {
                d.Dispose();
            }
        };
    }
}

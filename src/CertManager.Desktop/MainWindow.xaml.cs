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

        Closed += (_, _) =>
        {
            if (DataContext is IDisposable d)
            {
                d.Dispose();
            }
        };
    }
}

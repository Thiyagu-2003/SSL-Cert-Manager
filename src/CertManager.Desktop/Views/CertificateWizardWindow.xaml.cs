using System.Windows;
using CertManager.Desktop.ViewModels;

namespace CertManager.Desktop.Views;

public partial class CertificateWizardWindow : Window
{
    public CertificateWizardWindow(CertificateWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

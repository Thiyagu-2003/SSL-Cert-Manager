using System.Windows;
using CertManager.Desktop.ViewModels;

namespace CertManager.Desktop.Views;

public partial class InitialSetupWindow : Window
{
    private readonly InitialSetupViewModel _viewModel;

    public InitialSetupWindow(InitialSetupViewModel? viewModel = null)
    {
        InitializeComponent();
        _viewModel = viewModel ?? new InitialSetupViewModel();
        DataContext = _viewModel;

        _viewModel.RequestClose += () =>
        {
            DialogResult = _viewModel.DialogResult;
            Close();
        };
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

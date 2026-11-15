using System;
using System.IO;
using System.Windows;

namespace CertManager.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), args.ExceptionObject.ToString());
        };

        DispatcherUnhandledException += (s, args) =>
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), args.Exception.ToString());
            args.Handled = false;
        };

        base.OnStartup(e);
    }
}


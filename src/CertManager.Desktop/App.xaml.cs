using System;
using System.IO;
using System.Windows;
using CertManager.Service;

namespace CertManager.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash(args.ExceptionObject);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = false;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (ServiceHostBuilder.CurrentInProcessHost is { } host)
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                host.StopAsync(cts.Token).GetAwaiter().GetResult();
                host.Dispose();
            }
        }
        catch
        {
            // Ignore during process shutdown
        }

        base.OnExit(e);
    }

    private static void LogCrash(object? ex)
    {
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CertManager");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"), $"[{DateTime.UtcNow:O}] {ex}\n\n");
        }
        catch
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), $"[{DateTime.UtcNow:O}] {ex}\n\n");
            }
            catch
            {
                // Silently ignore if unable to write crash log
            }
        }
    }
}


using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CertManager.Service;
using Microsoft.Extensions.Hosting;

namespace CertManager.Desktop;

public static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            string cmd = args[0].ToLowerInvariant();

            if (cmd is "--service" or "-s" or "/service")
            {
                // Run headless Windows Service host
                var host = ServiceHostBuilder.BuildHost(args.Skip(1).ToArray());
                host.Run();
                return;
            }

            if (cmd is "--install-service" or "/install-service")
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                InstallService();
                return;
            }

            if (cmd is "--uninstall-service" or "/uninstall-service")
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                UninstallService();
                return;
            }

            if (cmd is "--help" or "-h" or "/?" or "-help")
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                PrintHelp();
                return;
            }
        }

        // Launch standard WPF GUI application
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static void InstallService()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName 
            ?? Path.Combine(AppContext.BaseDirectory, "CertFlow.exe");

        Console.WriteLine("=========================================================");
        Console.WriteLine(" CertFlow — Windows Background Service Installer");
        Console.WriteLine("=========================================================");
        Console.WriteLine($"Target binary: {exePath}");

        // sc create CertFlow binPath= "\"C:\path\CertFlow.exe\" --service" start= auto DisplayName= "CertFlow SSL/TLS Automation Service"
        string binPathArg = $"\"\\\"{exePath}\\\" --service\"";
        RunScCommand($"create CertFlow binPath= {binPathArg} start= auto DisplayName= \"CertFlow SSL/TLS Automation Service\"");
        RunScCommand("description CertFlow \"Automates SSL/TLS certificate ordering, Let's Encrypt renewal, ACME challenge validation, and IIS bindings.\"");
        RunScCommand("start CertFlow");

        Console.WriteLine("\n[OK] CertFlow Windows Service successfully installed and started.");
        Console.WriteLine("     To manage the service, use 'CertFlow.exe --uninstall-service' or Windows Services (services.msc).\n");
    }

    private static void UninstallService()
    {
        Console.WriteLine("=========================================================");
        Console.WriteLine(" CertFlow — Windows Background Service Uninstaller");
        Console.WriteLine("=========================================================");

        RunScCommand("stop CertFlow");
        RunScCommand("delete CertFlow");

        Console.WriteLine("\n[OK] CertFlow Windows Service removed.\n");
    }

    private static void RunScCommand(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(10000);
                string output = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine(output.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error executing sc.exe]: {ex.Message}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
CertFlow — Automated SSL/TLS Lifecycle Manager (Single EXE)

Usage:
  CertFlow.exe                    Launch interactive modern WPF Desktop UI
  CertFlow.exe --service          Run headless background service engine (ACME renewal & monitoring)
  CertFlow.exe --install-service  Install & start CertFlow as an automatic Windows Service
  CertFlow.exe --uninstall-service Stop & remove CertFlow Windows Service
  CertFlow.exe --help             Display this usage summary
");
    }
}

using System;
using System.IO;
using CertManager.Acme.Clients;
using CertManager.Application.Diagnostics;
using CertManager.Core.Interfaces;
using CertManager.Infrastructure.Certificates;
using CertManager.Infrastructure.Data;
using CertManager.Infrastructure.Diagnostics;
using CertManager.Infrastructure.Hooks;
using CertManager.Infrastructure.Monitoring;
using CertManager.Infrastructure.Notifications;
using CertManager.Infrastructure.Rds;
using CertManager.Integrations.Iis;
using CertManager.Integrations.Iis.Challenges;
using CertManager.Service.Services;
using CertManager.Service.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CertManager.Service;

public static class ServiceHostBuilder
{
    private static IHost? _inProcessHost;
    private static readonly SemaphoreSlim _hostLock = new(1, 1);

    public static async Task<IHost?> EnsureInProcessEngineRunningAsync()
    {
        if (_inProcessHost != null) return _inProcessHost;

        await _hostLock.WaitAsync();
        try
        {
            if (_inProcessHost != null) return _inProcessHost;

            var host = BuildHost();
            await host.StartAsync();
            _inProcessHost = host;
            return _inProcessHost;
        }
        catch
        {
            // If already started or named pipe occupied by external service, return current state
            return _inProcessHost;
        }
        finally
        {
            _hostLock.Release();
        }
    }

    public static IHost? CurrentInProcessHost => _inProcessHost;

    public static IHost BuildHost(string[]? args = null)
    {
        var builder = Host.CreateApplicationBuilder(args ?? Array.Empty<string>());

        // Configure database path in CommonApplicationData (e.g. C:\ProgramData\CertManager\certmanager.db)
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CertManager");

        if (!Directory.Exists(appDataDir))
        {
            try
            {
                Directory.CreateDirectory(appDataDir);
            }
            catch
            {
                appDataDir = AppContext.BaseDirectory;
            }
        }

        string dbPath = Path.Combine(appDataDir, "certmanager.db");

        builder.Services.AddDbContext<CertManagerDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}");
        });

        builder.Services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        builder.Services.AddSingleton<IIisServerAdapter, IisServerAdapter>();
        builder.Services.AddSingleton<IHttpChallengeServer, HttpChallengeServer>();
        builder.Services.AddSingleton<IHttp01ChallengeResponder, Http01ChallengeResponder>();
        builder.Services.AddSingleton<IAcmeCertificateClient, CertesAcmeClient>();
        builder.Services.AddSingleton<ICertificateInstaller, WindowsCertificateInstaller>();
        builder.Services.AddSingleton<ICertificateExportService, CertificateExportService>();
        builder.Services.AddSingleton<ICertificateIssuanceWorkflow, CertificateIssuanceWorkflow>();
        builder.Services.AddSingleton<IHttpsHealthChecker, HttpsHealthChecker>();
        builder.Services.AddScoped<INotificationProvider, NotificationDispatcher>();
        builder.Services.AddScoped<ISiteAutoDiscoveryEngine, SiteAutoDiscoveryEngine>();
        builder.Services.AddSingleton<IDomainReadinessProbe, DefaultDomainReadinessProbe>();
        builder.Services.AddSingleton<IDomainReadinessChecker, DomainReadinessChecker>();
        builder.Services.AddSingleton<IRdsCertificateBinder, RdsCertificateBinder>();
        builder.Services.AddSingleton<ILiveTlsInspector, LiveTlsInspector>();
        builder.Services.AddSingleton<IRenewalHookExecutor, RenewalHookExecutor>();

        // Hosted background services
        builder.Services.AddHostedService<Worker>();
        builder.Services.AddHostedService<IpcHostService>();

        return builder.Build();
    }
}

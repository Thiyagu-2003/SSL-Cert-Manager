using System;
using System.IO;
using CertManager.Infrastructure.Data;
using CertManager.Integrations.Iis;
using CertManager.Service;
using CertManager.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

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
        // Fallback to local directory if permissions or non-elevated dev mode
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

// Hosted background services
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<IpcHostService>();

var host = builder.Build();
host.Run();

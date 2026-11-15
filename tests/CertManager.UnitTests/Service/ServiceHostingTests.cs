using System;
using System.IO;
using CertManager.Core.Ipc;
using CertManager.Infrastructure.Data;
using CertManager.Infrastructure.Ipc;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CertManager.UnitTests.Service;

public class ServiceHostingTests
{
    [Fact]
    public void ServiceContainer_ResolvesAllKeyDependencies()
    {
        var services = new ServiceCollection();
        string tempDb = Path.Combine(Path.GetTempPath(), $"certmanager_host_test_{Guid.NewGuid():N}.db");

        services.AddDbContext<CertManagerDbContext>(options =>
            options.UseSqlite($"Data Source={tempDb}"));

        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        services.AddSingleton<INamedPipeServer>(sp =>
            new NamedPipeServer("CertManager.TestPipe", (req, identity) =>
                Task.FromResult(IpcResponse.Ok(req.RequestId))));

        var provider = services.BuildServiceProvider();

        var dbContext = provider.GetService<CertManagerDbContext>();
        var dbInit = provider.GetService<IDatabaseInitializer>();
        var ipcServer = provider.GetService<INamedPipeServer>();

        dbContext.Should().NotBeNull();
        dbInit.Should().NotBeNull();
        ipcServer.Should().NotBeNull();

        if (File.Exists(tempDb))
        {
            try { File.Delete(tempDb); } catch { }
        }
    }
}

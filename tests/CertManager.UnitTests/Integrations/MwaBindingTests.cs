using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Acme.Clients;
using CertManager.Core.Entities;
using CertManager.Core.Interfaces;
using CertManager.Core.Models;
using CertManager.Infrastructure.Certificates;
using CertManager.Infrastructure.Data;
using CertManager.Integrations.Iis;
using CertManager.Integrations.Iis.Challenges;
using CertManager.Service.Workflows;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.Administration;
using Xunit;

namespace CertManager.UnitTests.Integrations;

public class MwaBindingTests
{
    [Fact]
    public void CheckSslFlagsEnum()
    {
        var flag = SslFlags.Sni;
        Assert.Equal(1, (int)flag);
    }

    [Fact]
    public async Task Http01ChallengeResponder_GeneratesValidWebConfigWithoutStarMime()
    {
        var responder = new Http01ChallengeResponder();
        string tempDir = Path.Combine(Path.GetTempPath(), "certflow_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await responder.ProvisionChallengeAsync(tempDir, "test-token", "test-auth", CancellationToken.None);
            string webConfig = Path.Combine(tempDir, ".well-known", "acme-challenge", "web.config");
            File.Exists(webConfig).Should().BeTrue();

            string content = await File.ReadAllTextAsync(webConfig);
            content.Should().Contain("fileExtension=\".\"");
            content.Should().NotContain("fileExtension=\"*\"");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task CertificateIssuanceWorkflow_ExecutesSelfSignedSuccessfully()
    {
        var services = new ServiceCollection();
        string dbPath = Path.Combine(Path.GetTempPath(), $"certflow_test_{Guid.NewGuid():N}.db");
        services.AddDbContext<CertManagerDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();
            db.Database.EnsureCreated();
        }

        var acmeFake = new FakeAcmeCertificateClient();
        var iisFake = new FakeIisServerAdapter();
        var responderFake = new FakeHttp01ChallengeResponder();
        var installer = new WindowsCertificateInstaller();

        var workflow = new CertificateIssuanceWorkflow(
            acmeFake,
            iisFake,
            responderFake,
            installer,
            sp,
            NullLogger<CertificateIssuanceWorkflow>.Instance
        );

        var request = new CertificateRequestModel
        {
            SiteId = 1,
            DomainName = "local-test.internal",
            ContactEmail = "admin@local-test.internal",
            ValidationMethod = "SelfSigned"
        };

        var result = await workflow.ExecuteAsync(request);
        result.Success.Should().BeTrue();
        result.Thumbprint.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task LiveIis_SelfSignedCertificateIssuanceAndBinding_Succeeds()
    {
        var iisAdapter = new IisServerAdapter();
        if (!iisAdapter.IsIisAvailable()) return;

        var services = new ServiceCollection();
        string dbPath = Path.Combine(Path.GetTempPath(), $"certflow_test_{Guid.NewGuid():N}.db");
        services.AddDbContext<CertManagerDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CertManagerDbContext>();
            db.Database.EnsureCreated();
        }

        var workflow = new CertificateIssuanceWorkflow(
            new FakeAcmeCertificateClient(),
            iisAdapter,
            new FakeHttp01ChallengeResponder(),
            new WindowsCertificateInstaller(),
            sp,
            NullLogger<CertificateIssuanceWorkflow>.Instance
        );

        var request = new CertificateRequestModel
        {
            SiteId = 1,
            DomainName = "test.certflow.local",
            ContactEmail = "admin@certflow.local",
            ValidationMethod = "SelfSigned"
        };

        try
        {
            var result = await workflow.ExecuteAsync(request);
            result.Success.Should().BeTrue();
            result.Thumbprint.Should().NotBeNullOrWhiteSpace();

            // Verify binding was created on Default Web Site (Site 1)
            var sites = await iisAdapter.DiscoverDetailedWebsitesAsync();
            var defaultSite = sites.FirstOrDefault(s => s.SiteId == 1);
            defaultSite.Should().NotBeNull();
            defaultSite!.Bindings.Should().Contain(b => b.Protocol == "https" && b.Hostname == "test.certflow.local" && b.Port == 443);
        }
        finally
        {
            // Clean up test binding and test database
            try
            {
                using var sm = new ServerManager();
                var site = sm.Sites.FirstOrDefault(s => s.Id == 1);
                var testBinding = site?.Bindings.FirstOrDefault(b => b.Host == "test.certflow.local" && b.Protocol == "https");
                if (testBinding != null)
                {
                    site!.Bindings.Remove(testBinding);
                    sm.CommitChanges();
                }
            }
            catch { }

            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    private class FakeAcmeCertificateClient : IAcmeCertificateClient
    {
        public Task<string> InitializeAccountAsync(string email, bool isStaging, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://acme.test/account/1");

        public Task<IReadOnlyList<AcmeChallengeItem>> CreateOrderAndGetChallengesAsync(
            IReadOnlyList<string> domains, string validationMethod = "Http01", CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AcmeChallengeItem>>(Array.Empty<AcmeChallengeItem>());

        public Task<bool> SubmitChallengeAsync(AcmeChallengeItem challengeItem, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<byte[]> FinalizeOrderAndExportPfxAsync(
            string friendlyName, string password, IReadOnlyList<string>? sanDomains = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());
    }

    private class FakeIisServerAdapter : IIisServerAdapter
    {
        public bool IsIisAvailable() => false;
        public string? GetWebsitePhysicalPath(long siteId) => null;
        public Task<IReadOnlyList<ManagedWebsite>> DiscoverWebsitesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedWebsite>>(Array.Empty<ManagedWebsite>());
        public Task<IReadOnlyList<DiscoveredIisSite>> DiscoverDetailedWebsitesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredIisSite>>(Array.Empty<DiscoveredIisSite>());
        public Task<DeploymentResult> DeployCertificateAsync(CertificateReference certificate, DeploymentTarget target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeploymentResult(true, null, null));
        public Task<HealthCheckResult> VerifyAsync(DeploymentTarget target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(true, "OK"));
    }

    private class FakeHttp01ChallengeResponder : IHttp01ChallengeResponder
    {
        public Task<string> ProvisionChallengeAsync(string webRootPath, string token, string keyAuth, CancellationToken cancellationToken = default) =>
            Task.FromResult(Path.Combine(webRootPath, token));
        public Task CleanupChallengeAsync(string webRootPath, string token, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

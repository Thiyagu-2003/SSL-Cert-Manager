using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CertManager.Core.Models;
using CertManager.Infrastructure.Diagnostics;
using CertManager.Infrastructure.Hooks;
using CertManager.Infrastructure.Rds;
using CertManager.Infrastructure.Security;
using CertManager.Infrastructure.Vault;
using CertManager.Integrations.Dns;
using Xunit;

namespace CertManager.UnitTests.Infrastructure;

public class AdvancedFeaturesTests
{
    [Fact]
    public async Task WindowsSecurityHardener_Audit_ReturnsValidReport()
    {
        var hardener = new WindowsSecurityHardener();
        var report = await hardener.AuditSecurityAsync();

        Assert.NotNull(report);
        Assert.NotNull(report.OverallGrade);
        Assert.NotEmpty(report.Protocols);
        Assert.NotEmpty(report.Ciphers);
        Assert.NotEmpty(report.Recommendations);
    }

    [Fact]
    public async Task RenewalHookExecutor_ResolvesParametersAndExecutes()
    {
        var executor = new RenewalHookExecutor();
        var config = new RenewalHookConfig
        {
            HookType = RenewalHookType.BatchScript,
            ScriptOrTarget = "echo",
            Arguments = "Cert for {Domain} with thumbprint {Thumbprint} expiring {Expires}"
        };

        var result = await executor.ExecuteHookAsync(
            config, 
            domain: "app.test.com", 
            thumbprint: "11223344AABBCC", 
            expires: new DateTime(2027, 1, 1));

        Assert.NotNull(result);
        Assert.Equal(config.Id, result.HookId);
    }

    [Fact]
    public async Task LiveTlsInspector_EmptyOrInvalidDomain_ThrowsArgumentException()
    {
        var inspector = new LiveTlsInspector();
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.InspectLiveDomainAsync(""));
    }

    [Fact]
    public async Task RdsCertificateBinder_EmptyThumbprint_ThrowsArgumentException()
    {
        var binder = new RdsCertificateBinder();
        await Assert.ThrowsAsync<ArgumentException>(() => binder.BindCertificateToRdsAsync(""));
    }

    [Fact]
    public async Task RdsCertificateBinder_InspectRds_ReturnsInfo()
    {
        var binder = new RdsCertificateBinder();
        var info = await binder.GetCurrentRdsCertificateAsync();
        Assert.NotNull(info);
        Assert.NotNull(info.StatusMessage);
    }

    [Fact]
    public async Task Route53DnsProvider_CreatesAndListsTxtRecords()
    {
        var provider = new Route53DnsProvider("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
        var zones = await provider.GetZonesAsync();
        Assert.NotEmpty(zones);

        var record = await provider.CreateTxtRecordAsync("Z123", "_acme-challenge.example.com", "challenge-token-xyz");
        Assert.NotNull(record);
        Assert.Equal("TXT", record.Type);
        Assert.Equal("challenge-token-xyz", record.Value);
    }

    [Fact]
    public async Task AzureDnsProvider_CreatesAndListsTxtRecords()
    {
        var provider = new AzureDnsProvider("sub-123", "rg-certs", "app-client", "secret-xyz", "tenant-123");
        var zones = await provider.GetZonesAsync();
        Assert.NotEmpty(zones);

        var record = await provider.CreateTxtRecordAsync("zone-1", "_acme-challenge", "challenge-token-abc");
        Assert.NotNull(record);
        Assert.Equal("TXT", record.Type);
        Assert.Equal("challenge-token-abc", record.Value);
    }

    [Fact]
    public async Task VaultBackupManager_ExportAndImport_RoundTripSucceeds()
    {
        var manager = new VaultBackupManager();
        string tempFile = Path.Combine(Path.GetTempPath(), $"certflow_test_vault_{Guid.NewGuid():N}.vault");
        string secretPassphrase = "SuperStrongPassphrase123!#";
        string originalPayload = "{\"TestKey\":\"TestValue\",\"AcmeAccounts\":[{\"Email\":\"admin@corp.com\"}]}";

        try
        {
            bool exportSuccess = await manager.ExportVaultToFileAsync(tempFile, secretPassphrase, originalPayload);
            Assert.True(exportSuccess);
            Assert.True(File.Exists(tempFile));

            string restoredJson = await manager.ImportVaultFromFileAsync(tempFile, secretPassphrase);
            Assert.Equal(originalPayload, restoredJson);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VaultBackupManager_WrongPassphrase_FailsDecryption()
    {
        var manager = new VaultBackupManager();
        string tempFile = Path.Combine(Path.GetTempPath(), $"certflow_test_vault_{Guid.NewGuid():N}.vault");
        string originalPayload = "{\"Secret\":\"SensitiveData\"}";

        try
        {
            await manager.ExportVaultToFileAsync(tempFile, "CorrectPassword99", originalPayload);

            await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
            {
                await manager.ImportVaultFromFileAsync(tempFile, "WrongPassword00");
            });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

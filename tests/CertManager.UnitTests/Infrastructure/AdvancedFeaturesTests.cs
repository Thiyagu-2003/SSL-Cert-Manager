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

    [Fact]
    public async Task VaultBackupManager_ExportToNonExistentDirectory_CreatesDirectoryAndSucceeds()
    {
        var manager = new VaultBackupManager();
        string tempDir = Path.Combine(Path.GetTempPath(), $"certflow_nested_{Guid.NewGuid():N}", "subfolder");
        string tempFile = Path.Combine(tempDir, "vault.vault");
        string originalPayload = "{\"Test\":true}";

        try
        {
            bool success = await manager.ExportVaultToFileAsync(tempFile, "pass12345", originalPayload);
            Assert.True(success);
            Assert.True(File.Exists(tempFile));

            string readBack = await manager.ImportVaultFromFileAsync(tempFile, "pass12345");
            Assert.Equal(originalPayload, readBack);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RenewalTimelineInfo_ZeroLifetime_DoesNotReturnNaN()
    {
        var cert = new CertManager.Core.Entities.CertificateRecord
        {
            NotBefore = DateTime.UtcNow,
            NotAfter = DateTime.UtcNow // zero lifetime
        };

        var info = RenewalTimelineInfo.FromCertificate(cert);
        Assert.False(double.IsNaN(info.ProgressPercent));
        Assert.Equal(100.0, info.ProgressPercent);
        Assert.Equal(RenewalTimelineZone.Expired, info.CurrentZone);
    }

    [Fact]
    public async Task RenewalHookExecutor_ServiceRestart_RejectsInjectionDelimiters()
    {
        var executor = new RenewalHookExecutor();
        var config = new RenewalHookConfig
        {
            HookType = RenewalHookType.WindowsServiceRestart,
            ScriptOrTarget = "MyService & calc.exe"
        };

        var result = await executor.ExecuteHookAsync(config, "domain.com", "112233");
        Assert.False(result.Success);
        Assert.Contains("Invalid service name", result.StandardError);
    }

    [Fact]
    public async Task RenewalHookExecutor_NullParameters_DoesNotThrow()
    {
        var executor = new RenewalHookExecutor();
        var config = new RenewalHookConfig
        {
            HookType = RenewalHookType.BatchScript,
            ScriptOrTarget = "echo",
            Arguments = "{Domain} {Thumbprint} {PfxPath} {Expires}"
        };

        var result = await executor.ExecuteHookAsync(config, null!, null!, null, null);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RdsCertificateBinder_InvalidThumbprintLength_ReturnsFailure()
    {
        var binder = new RdsCertificateBinder();
        var result = await binder.BindCertificateToRdsAsync("SHORT_THUMB");
        Assert.False(result.Success);
        Assert.Contains("40-character", result.Message);
    }

    [Fact]
    public async Task WindowsSecurityHardener_ApplyHardening_NullOptions_ThrowsArgumentNullException()
    {
        var hardener = new WindowsSecurityHardener();
        await Assert.ThrowsAsync<ArgumentNullException>(() => hardener.ApplyHardeningAsync(null!));
    }

    [Fact]
    public async Task RdsCertificateBinder_NonHexThumbprint_ReturnsFailure()
    {
        var binder = new RdsCertificateBinder();
        // 40 characters but non-hex 'G'
        var result = await binder.BindCertificateToRdsAsync("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG");
        Assert.False(result.Success);
        Assert.Contains("40-character", result.Message);
    }

    [Fact]
    public async Task RenewalHookExecutor_NonExistentScript_ReturnsFailure()
    {
        var executor = new RenewalHookExecutor();
        var config = new RenewalHookConfig
        {
            HookType = RenewalHookType.PowerShellScript,
            ScriptOrTarget = "C:\\non_existent_folder_xyz_123\\test_hook.ps1"
        };

        var result = await executor.ExecuteHookAsync(config, "app.test.com", "112233");
        Assert.False(result.Success);
        Assert.Contains("not found", result.StandardError);
    }

    [Fact]
    public async Task CertesAcmeClient_NullOrEmptySerial_ReturnsNullAri()
    {
        var client = new CertManager.Acme.Clients.CertesAcmeClient();
        var ari = await client.GetRenewalInfoAsync("");
        Assert.Null(ari);
    }

    [Fact]
    public async Task Route53DnsProvider_IsTxtRecordVisibleAsync_NonExistent_ReturnsFalse()
    {
        var provider = new Route53DnsProvider("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
        bool visible = await provider.IsTxtRecordVisibleAsync("_acme-challenge.nonexistent-domain-test-12345.com", "some-value");
        Assert.False(visible);
    }

    [Fact]
    public async Task AzureDnsProvider_IsTxtRecordVisibleAsync_NonExistent_ReturnsFalse()
    {
        var provider = new AzureDnsProvider("sub-1", "rg-1", "client-1", "sec-1", "ten-1");
        bool visible = await provider.IsTxtRecordVisibleAsync("_acme-challenge.nonexistent-domain-test-12345.com", "some-value");
        Assert.False(visible);
    }

    [Fact]
    public async Task CertificateExportService_NullRequest_ThrowsArgumentNullException()
    {
        var exportService = new CertManager.Infrastructure.Certificates.CertificateExportService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => exportService.ExportCertificateAsync(null!));
    }

    [Fact]
    public async Task CertificateExportService_EmptyThumbprint_ReturnsFailure()
    {
        var exportService = new CertManager.Infrastructure.Certificates.CertificateExportService();
        var request = new CertManager.Core.Interfaces.CertificateExportRequest(
            "",
            "example.com",
            CertManager.Core.Interfaces.CertificateExportFormat.Pfx,
            "C:\\Temp");
        var result = await exportService.ExportCertificateAsync(request);
        Assert.False(result.Success);
        Assert.Contains("Thumbprint cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task CertificateExportService_NonExistentCertificate_ReturnsNotFound()
    {
        var exportService = new CertManager.Infrastructure.Certificates.CertificateExportService();
        var request = new CertManager.Core.Interfaces.CertificateExportRequest(
            "0000000000000000000000000000000000000000",
            "*.example.com",
            CertManager.Core.Interfaces.CertificateExportFormat.Pfx,
            "C:\\Temp");
        var result = await exportService.ExportCertificateAsync(request);
        Assert.False(result.Success);
        Assert.Contains("was not found", result.ErrorMessage);
    }

    [Fact]
    public async Task LiveTlsInspector_EmptyOrWhitespaceDomain_ThrowsArgumentException()
    {
        var inspector = new CertManager.Infrastructure.Diagnostics.LiveTlsInspector();
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.InspectLiveDomainAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.InspectLiveDomainAsync("   "));
    }

    [Fact]
    public async Task LiveTlsInspector_UnreachableHost_ReturnsGradeF()
    {
        var inspector = new CertManager.Infrastructure.Diagnostics.LiveTlsInspector();
        var result = await inspector.InspectLiveDomainAsync("invalid-host-name-that-cannot-exist.local");
        Assert.False(result.IsReachable);
        Assert.Equal("F", result.OverallGrade);
    }
}

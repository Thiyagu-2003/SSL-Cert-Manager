using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CertManager.Infrastructure.Certificates;

public class CertificateExportService : ICertificateExportService
{
    private readonly ILogger<CertificateExportService> _logger;

    public CertificateExportService(ILogger<CertificateExportService>? logger = null)
    {
        _logger = logger ?? NullLogger<CertificateExportService>.Instance;
    }

    public Task<CertificateExportResult> ExportCertificateAsync(
        CertificateExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Thumbprint))
        {
            return Task.FromResult(new CertificateExportResult(false, null, "Thumbprint cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.TargetDirectoryOrFilePath))
        {
            return Task.FromResult(new CertificateExportResult(false, null, "Target destination path cannot be empty."));
        }

        try
        {
            using var cert = FindCertificateInStore(request.Thumbprint);
            if (cert == null)
            {
                return Task.FromResult(new CertificateExportResult(false, null, $"Certificate with thumbprint '{request.Thumbprint}' was not found in the Windows Certificate Store."));
            }

            string domainSlug = !string.IsNullOrWhiteSpace(request.DomainName)
                ? request.DomainName.Replace('*', '_').Trim()
                : (cert.GetNameInfo(X509NameType.DnsName, false) ?? cert.Thumbprint);

            switch (request.Format)
            {
                case CertificateExportFormat.Pfx:
                {
                    string targetFile = request.TargetDirectoryOrFilePath;
                    if (Directory.Exists(targetFile) || !Path.HasExtension(targetFile))
                    {
                        Directory.CreateDirectory(targetFile);
                        targetFile = Path.Combine(targetFile, $"{domainSlug}.pfx");
                    }
                    else
                    {
                        string? parent = Path.GetDirectoryName(targetFile);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    }

                    byte[] pfxBytes = cert.Export(X509ContentType.Pfx, request.Password ?? string.Empty);
                    File.WriteAllBytes(targetFile, pfxBytes);
                    _logger.LogInformation("Exported PFX certificate to: {Path}", targetFile);
                    return Task.FromResult(new CertificateExportResult(true, targetFile, null));
                }

                case CertificateExportFormat.PemBundle:
                {
                    string targetDir = request.TargetDirectoryOrFilePath;
                    if (File.Exists(targetDir) || Path.HasExtension(targetDir))
                    {
                        targetDir = Path.GetDirectoryName(targetDir) ?? AppContext.BaseDirectory;
                    }
                    Directory.CreateDirectory(targetDir);

                    string certPemPath = Path.Combine(targetDir, $"{domainSlug}.crt");
                    string keyPemPath = Path.Combine(targetDir, $"{domainSlug}.key");
                    string fullchainPemPath = Path.Combine(targetDir, $"{domainSlug}.fullchain.pem");

                    // 1. Export Certificate PEM
                    string certPem = cert.ExportCertificatePem();
                    File.WriteAllText(certPemPath, certPem, Encoding.UTF8);

                    // 2. Export Private Key PEM
                    string keyPem = string.Empty;
                    using var rsa = cert.GetRSAPrivateKey();
                    if (rsa != null)
                    {
                        keyPem = rsa.ExportPkcs8PrivateKeyPem();
                    }
                    else
                    {
                        using var ecdsa = cert.GetECDsaPrivateKey();
                        if (ecdsa != null)
                        {
                            keyPem = ecdsa.ExportPkcs8PrivateKeyPem();
                        }
                    }

                    if (!string.IsNullOrEmpty(keyPem))
                    {
                        File.WriteAllText(keyPemPath, keyPem, Encoding.UTF8);
                        // Full chain with key
                        File.WriteAllText(fullchainPemPath, certPem + "\n" + keyPem, Encoding.UTF8);
                    }
                    else
                    {
                        File.WriteAllText(fullchainPemPath, certPem, Encoding.UTF8);
                    }

                    _logger.LogInformation("Exported PEM bundle (CRT + KEY) to: {Path}", targetDir);
                    return Task.FromResult(new CertificateExportResult(true, targetDir, null));
                }

                case CertificateExportFormat.CentralCertificateStore:
                {
                    string ccsDir = request.TargetDirectoryOrFilePath;
                    Directory.CreateDirectory(ccsDir);

                    // Central Certificate Store requires <hostname>.pfx
                    string ccsFileName = $"{domainSlug}.pfx";
                    string targetFile = Path.Combine(ccsDir, ccsFileName);

                    byte[] pfxBytes = cert.Export(X509ContentType.Pfx, request.Password ?? string.Empty);
                    File.WriteAllBytes(targetFile, pfxBytes);
                    _logger.LogInformation("Exported certificate to Central Certificate Store (CCS): {Path}", targetFile);
                    return Task.FromResult(new CertificateExportResult(true, targetFile, null));
                }

                default:
                    return Task.FromResult(new CertificateExportResult(false, null, $"Unsupported format '{request.Format}'."));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export certificate: {Message}", ex.Message);
            return Task.FromResult(new CertificateExportResult(false, null, ex.Message));
        }
    }

    private static X509Certificate2? FindCertificateInStore(string thumbprint)
    {
        string cleanThumbprint = thumbprint.Replace(" ", "").Replace(":", "").Trim();

        // 1. Check LocalMachine\My
        using var machineStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        try
        {
            machineStore.Open(OpenFlags.ReadOnly);
            var matches = machineStore.Certificates.Find(X509FindType.FindByThumbprint, cleanThumbprint, false);
            if (matches.Count > 0)
            {
                return new X509Certificate2(matches[0]);
            }
        }
        catch { }

        // 2. Check CurrentUser\My fallback
        using var userStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        try
        {
            userStore.Open(OpenFlags.ReadOnly);
            var matches = userStore.Certificates.Find(X509FindType.FindByThumbprint, cleanThumbprint, false);
            if (matches.Count > 0)
            {
                return new X509Certificate2(matches[0]);
            }
        }
        catch { }

        return null;
    }
}

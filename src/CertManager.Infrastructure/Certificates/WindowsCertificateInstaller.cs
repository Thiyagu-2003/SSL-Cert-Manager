using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;

namespace CertManager.Infrastructure.Certificates;

public class WindowsCertificateInstaller : ICertificateInstaller
{
    public Task<InstalledCertificate> InstallAsync(
        CertificateBundle certificate,
        InstallOptions options,
        CancellationToken cancellationToken = default)
    {
        if (certificate.RawData == null || certificate.RawData.Length == 0)
        {
            throw new ArgumentException("Certificate raw data cannot be null or empty.", nameof(certificate));
        }

        if (!OperatingSystem.IsWindows())
        {
            // Dev/Testing fallback on non-Windows
            return Task.FromResult(new InstalledCertificate(certificate.Thumbprint, options.StoreName, options.StoreLocation));
        }

        var storeLocation = options.StoreLocation.Equals("CurrentUser", StringComparison.OrdinalIgnoreCase)
            ? StoreLocation.CurrentUser
            : StoreLocation.LocalMachine;

        var storeName = StoreName.My;
        if (Enum.TryParse<StoreName>(options.StoreName, true, out var parsedStoreName))
        {
            storeName = parsedStoreName;
        }

        using var x509Cert = new X509Certificate2(
            certificate.RawData,
            certificate.Password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

        string thumbprint = x509Cert.Thumbprint;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadWrite);

        // Avoid duplicate insertion
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        if (existing.Count == 0)
        {
            store.Add(x509Cert);
        }

        return Task.FromResult(new InstalledCertificate(thumbprint, storeName.ToString(), storeLocation.ToString()));
    }

    public Task<bool> IsInstalledAsync(
        CertificateReference certificate,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(true);
        }

        var storeLocation = certificate.StoreLocation.Equals("CurrentUser", StringComparison.OrdinalIgnoreCase)
            ? StoreLocation.CurrentUser
            : StoreLocation.LocalMachine;

        var storeName = StoreName.My;
        if (Enum.TryParse<StoreName>(certificate.StoreName, true, out var parsedStoreName))
        {
            storeName = parsedStoreName;
        }

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
        return Task.FromResult(matches.Count > 0);
    }
}

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
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(options);

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

        var keyStorageFlags = X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable;
        keyStorageFlags |= (storeLocation == StoreLocation.LocalMachine)
            ? X509KeyStorageFlags.MachineKeySet
            : X509KeyStorageFlags.UserKeySet;

        using var x509Cert = new X509Certificate2(
            certificate.RawData,
            certificate.Password,
            keyStorageFlags);

        string thumbprint = x509Cert.Thumbprint;

        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadWrite);

        // Avoid duplicate insertion
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        try
        {
            if (existing.Count == 0)
            {
                store.Add(x509Cert);
            }
        }
        finally
        {
            foreach (var c in existing)
            {
                c.Dispose();
            }
        }

        return Task.FromResult(new InstalledCertificate(thumbprint, storeName.ToString(), storeLocation.ToString()));
    }

    public Task<bool> IsInstalledAsync(
        CertificateReference certificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(true);
        }

        if (string.IsNullOrWhiteSpace(certificate.Thumbprint))
        {
            return Task.FromResult(false);
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
        try
        {
            return Task.FromResult(matches.Count > 0);
        }
        finally
        {
            foreach (var m in matches)
            {
                m.Dispose();
            }
        }
    }
}

using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Models;
using Microsoft.Win32;

namespace CertManager.Infrastructure.Rds;

public interface IRdsCertificateBinder
{
    Task<RdsBindingInfo> GetCurrentRdsCertificateAsync(CancellationToken cancellationToken = default);
    Task<RdsBindingResult> BindCertificateToRdsAsync(string thumbprint, CancellationToken cancellationToken = default);
}

public class RdsCertificateBinder : IRdsCertificateBinder
{
    private const string RdpTcpKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";

    public Task<RdsBindingInfo> GetCurrentRdsCertificateAsync(CancellationToken cancellationToken = default)
    {
        var info = new RdsBindingInfo();

        if (!OperatingSystem.IsWindows())
        {
            info.StatusMessage = "Remote Desktop Services is only available on Microsoft Windows.";
            return Task.FromResult(info);
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RdpTcpKey, false);
            if (key == null)
            {
                info.IsRdsInstalled = false;
                info.StatusMessage = "RDP-Tcp listener is not present on this machine.";
                return Task.FromResult(info);
            }

            info.IsRdsInstalled = true;
            object? val = key.GetValue("SSLCertificateSHA1Hash");

            if (val is byte[] hashBytes && hashBytes.Length == 20)
            {
                string thumbprint = Convert.ToHexString(hashBytes);
                info.CurrentThumbprint = thumbprint;

                // Lookup in LocalMachine\My
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                if (certs.Count > 0)
                {
                    var cert = certs[0];
                    info.CurrentCertSubject = cert.Subject;
                    info.CurrentCertExpires = cert.NotAfter;
                    info.StatusMessage = $"RDP is currently bound to: {cert.Subject} (Expires: {cert.NotAfter:yyyy-MM-dd})";
                }
                else
                {
                    info.StatusMessage = $"RDP is configured with thumbprint {thumbprint}, but certificate was not found in LocalMachine\\My.";
                }
            }
            else
            {
                info.StatusMessage = "RDP is currently using default Windows self-signed listener certificate.";
            }
        }
        catch (Exception ex)
        {
            info.StatusMessage = $"Failed to inspect RDP settings: {ex.Message}";
        }

        return Task.FromResult(info);
    }

    public Task<RdsBindingResult> BindCertificateToRdsAsync(string thumbprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);
        string cleanThumb = thumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant();

        var result = new RdsBindingResult { NewThumbprint = cleanThumb };

        if (!OperatingSystem.IsWindows())
        {
            result.Success = false;
            result.Message = "Operating system is not Windows.";
            return Task.FromResult(result);
        }

        try
        {
            // Verify certificate exists in LocalMachine\My
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, cleanThumb, false);
            if (matches.Count == 0)
            {
                result.Success = false;
                result.Message = $"Certificate with thumbprint {cleanThumb} was not found in LocalMachine\\My store.";
                return Task.FromResult(result);
            }

            byte[] hashBytes = Convert.FromHexString(cleanThumb);

            using (var key = Registry.LocalMachine.OpenSubKey(RdpTcpKey, true))
            {
                if (key == null)
                {
                    result.Success = false;
                    result.Message = "Terminal Server WinStations\\RDP-Tcp registry key could not be opened.";
                    return Task.FromResult(result);
                }

                object? prev = key.GetValue("SSLCertificateSHA1Hash");
                if (prev is byte[] pb)
                {
                    result.PreviousThumbprint = Convert.ToHexString(pb);
                }

                key.SetValue("SSLCertificateSHA1Hash", hashBytes, RegistryValueKind.Binary);
            }

            result.Success = true;
            result.Message = $"Successfully bound certificate {cleanThumb} to Windows Remote Desktop (RDP-Tcp) listener.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Failed to bind RDP certificate: {ex.Message}";
        }

        return Task.FromResult(result);
    }
}

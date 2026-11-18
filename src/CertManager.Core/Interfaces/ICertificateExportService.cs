using System.Threading;
using System.Threading.Tasks;

namespace CertManager.Core.Interfaces;

public enum CertificateExportFormat
{
    Pfx,
    PemBundle,
    CentralCertificateStore
}

public record CertificateExportRequest(
    string Thumbprint,
    string TargetDirectoryOrFilePath,
    CertificateExportFormat Format,
    string? Password = null,
    string? DomainName = null);

public record CertificateExportResult(
    bool Success,
    string? ExportedPath,
    string? ErrorMessage);

public interface ICertificateExportService
{
    Task<CertificateExportResult> ExportCertificateAsync(CertificateExportRequest request, CancellationToken cancellationToken = default);
}

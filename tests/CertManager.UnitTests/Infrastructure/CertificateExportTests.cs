using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CertManager.Core.Interfaces;
using CertManager.Infrastructure.Certificates;
using FluentAssertions;
using Xunit;

namespace CertManager.UnitTests.Infrastructure;

public class CertificateExportTests
{
    [Fact]
    public async Task ExportCertificateAsync_FailsGracefully_WhenThumbprintNotFound()
    {
        var service = new CertificateExportService();
        var request = new CertificateExportRequest(
            "0000000000000000000000000000000000000000",
            Path.GetTempPath(),
            CertificateExportFormat.Pfx);

        var result = await service.ExportCertificateAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ExportCertificateAsync_FailsGracefully_WhenPathIsEmpty()
    {
        var service = new CertificateExportService();
        var request = new CertificateExportRequest(
            "123456",
            "",
            CertificateExportFormat.Pfx);

        var result = await service.ExportCertificateAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("path cannot be empty");
    }
}

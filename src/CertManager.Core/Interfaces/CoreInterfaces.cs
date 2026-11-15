using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CertManager.Core.Interfaces;

public record AcmeAccountOptions(string Email, bool AcceptTermsOfService, string DirectoryUrl);
public record AcmeAccountInfo(string AccountId, string Email, string DirectoryUrl, bool IsValid);
public record CertificateRequest(
    Guid ProfileId,
    IReadOnlyList<string> Identifiers,
    string KeyAlgorithm,
    string KeySizeOrCurve);
public record CertificateOrderResult(
    Guid OrderId,
    string OrderReference,
    string Status,
    IReadOnlyList<string> Authorizations);
public record CertificateStatus(string Status, DateTime? ExpiresAt, string? SerialNumber);
public record CertificateBundle(byte[] RawData, string Password, string Thumbprint);
public record InstallOptions(string StoreName, string StoreLocation);
public record InstalledCertificate(string Thumbprint, string StoreName, string StoreLocation);
public record CertificateReference(string Thumbprint, string StoreName, string StoreLocation);
public record ManagedWebsite(long SiteId, string Name, string State, IReadOnlyList<string> Bindings);
public record DeploymentTarget(long SiteId, string Hostname, int Port, string IpAddress);
public record DeploymentResult(bool Success, string? ErrorMessage, string? PreviousThumbprint);
public record HealthCheckResult(bool IsHealthy, string? StatusMessage);
public record NotificationMessage(string Subject, string Body, string Level);

public record DnsZone(string Id, string Name, string Status);
public record DnsRecord(string Id, string ZoneId, string Name, string Type, string Value, int Ttl);

public interface IDnsProvider
{
    string ProviderName { get; }
    Task<IReadOnlyList<DnsZone>> GetZonesAsync(CancellationToken cancellationToken = default);
    Task<DnsRecord> CreateTxtRecordAsync(string zoneId, string name, string value, CancellationToken cancellationToken = default);
    Task DeleteTxtRecordAsync(string zoneId, string recordId, CancellationToken cancellationToken = default);
    Task<bool> IsTxtRecordVisibleAsync(string name, string expectedValue, CancellationToken cancellationToken = default);
}

public interface ICertificateAuthorityClient
{
    Task<AcmeAccountInfo> RegisterAccountAsync(AcmeAccountOptions options, CancellationToken cancellationToken = default);
    Task<CertificateOrderResult> RequestCertificateAsync(CertificateRequest request, CancellationToken cancellationToken = default);
    Task<CertificateStatus> GetOrderStatusAsync(string orderReference, CancellationToken cancellationToken = default);
}

public interface ICertificateInstaller
{
    Task<InstalledCertificate> InstallAsync(CertificateBundle certificate, InstallOptions options, CancellationToken cancellationToken = default);
    Task<bool> IsInstalledAsync(CertificateReference certificate, CancellationToken cancellationToken = default);
}

public interface IServerAdapter
{
    Task<IReadOnlyList<ManagedWebsite>> DiscoverWebsitesAsync(CancellationToken cancellationToken = default);
    Task<DeploymentResult> DeployCertificateAsync(CertificateReference certificate, DeploymentTarget target, CancellationToken cancellationToken = default);
    Task<HealthCheckResult> VerifyAsync(DeploymentTarget target, CancellationToken cancellationToken = default);
}

public interface INotificationProvider
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

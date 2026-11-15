using System;
using System.Collections.Generic;
using CertManager.Core.Enums;

namespace CertManager.Core.Entities;

public class Domain
{
    public Guid DomainId { get; set; } = Guid.NewGuid();
    public string DomainName { get; set; } = string.Empty; // Normalized A-label
    public string DisplayName { get; set; } = string.Empty; // Unicode form
    public string RootDomain { get; set; } = string.Empty;
    public DomainType DomainType { get; set; }
    public string? DnsProviderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProfileDomain> ProfileDomains { get; set; } = new List<ProfileDomain>();
    public ICollection<CertificateDomain> CertificateDomains { get; set; } = new List<CertificateDomain>();
}

public class CertificateProfile
{
    public Guid ProfileId { get; set; } = Guid.NewGuid();
    public string ProfileName { get; set; } = string.Empty;
    public string? AcmeAccountId { get; set; }
    public string AuthorityId { get; set; } = "letsencrypt";
    public ValidationMethod DefaultValidationMethod { get; set; } = ValidationMethod.Http01;
    public string? DefaultDnsProviderId { get; set; }
    public string KeyAlgorithm { get; set; } = "RSA";
    public string KeySizeOrCurve { get; set; } = "2048";
    public int RenewalThresholdPercent { get; set; } = 33;
    public bool UseAri { get; set; } = true;
    public string? ServerId { get; set; }
    public string TargetStoreName { get; set; } = "My";
    public string TargetStoreLocation { get; set; } = "LocalMachine";
    public string DeploymentAdapter { get; set; } = "IIS";
    public string? DeploymentSettingsJson { get; set; }
    public Guid? NotificationPolicyId { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProfileDomain> Domains { get; set; } = new List<ProfileDomain>();
    public ICollection<CertificateRecord> Certificates { get; set; } = new List<CertificateRecord>();
    public ICollection<AcmeOrder> AcmeOrders { get; set; } = new List<AcmeOrder>();
}

public class ProfileDomain
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public CertificateProfile? Profile { get; set; }

    public Guid DomainId { get; set; }
    public Domain? Domain { get; set; }

    public DomainType IdentifierType { get; set; }
    public ValidationMethod? ValidationMethodOverride { get; set; }
    public string? DnsProviderIdOverride { get; set; }
}

public class CertificateRecord
{
    public Guid CertificateId { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public CertificateProfile? Profile { get; set; }

    public string? ServerId { get; set; }
    public Guid? AcmeOrderId { get; set; }
    public AcmeOrder? AcmeOrder { get; set; }

    public Guid? PreviousCertificateId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public string KeyAlgorithm { get; set; } = string.Empty;
    public string StoreName { get; set; } = "My";
    public string StoreLocation { get; set; } = "LocalMachine";
    public CertificateStatus Status { get; set; } = CertificateStatus.Active;
    public DateTime? RetainUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CertificateDomain> CertificateDomains { get; set; } = new List<CertificateDomain>();
}

public class CertificateDomain
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CertificateId { get; set; }
    public CertificateRecord? Certificate { get; set; }

    public Guid DomainId { get; set; }
    public Domain? Domain { get; set; }

    public DomainType IdentifierType { get; set; }
}

public class AcmeOrder
{
    public Guid AcmeOrderId { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public CertificateProfile? Profile { get; set; }

    public string? AcmeAccountId { get; set; }
    public string? OrderReference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public AcmeOrderStatus Status { get; set; } = AcmeOrderStatus.Pending;
    public DateTime? RetryAfterUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }

    public ICollection<AcmeChallenge> Challenges { get; set; } = new List<AcmeChallenge>();
}

public class AcmeChallenge
{
    public Guid ChallengeId { get; set; } = Guid.NewGuid();
    public Guid AcmeOrderId { get; set; }
    public AcmeOrder? AcmeOrder { get; set; }

    public Guid DomainId { get; set; }
    public Domain? Domain { get; set; }

    public ValidationMethod ChallengeType { get; set; }
    public AcmeChallengeStatus Status { get; set; } = AcmeChallengeStatus.Pending;
    public string? ProviderRecordId { get; set; }
    public ChallengeCleanupState CleanupState { get; set; } = ChallengeCleanupState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class WorkflowStep
{
    public Guid StepId { get; set; } = Guid.NewGuid();
    public Guid OperationId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public WorkflowStepState State { get; set; } = WorkflowStepState.Pending;
    public int Attempt { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? SafeDetails { get; set; }
}

public class WorkflowLock
{
    public string LockKey { get; set; } = string.Empty; // Primary Key
    public Guid OwnerOperationId { get; set; }
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
    public DateTime LeaseExpiresAt { get; set; }
}

public class IisWebsite
{
    public Guid WebsiteId { get; set; } = Guid.NewGuid();
    public string? ServerId { get; set; }
    public long IisSiteId { get; set; }
    public string WebsiteName { get; set; } = string.Empty;
    public string State { get; set; } = "Started";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastDiscoveredAt { get; set; } = DateTime.UtcNow;

    public ICollection<IisBinding> Bindings { get; set; } = new List<IisBinding>();
}

public class IisBinding
{
    public Guid BindingId { get; set; } = Guid.NewGuid();
    public Guid WebsiteId { get; set; }
    public IisWebsite? Website { get; set; }

    public Guid? CertificateId { get; set; }
    public CertificateRecord? Certificate { get; set; }

    public Guid? OwnerProfileId { get; set; }
    public string Protocol { get; set; } = "https";
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "*";
    public int Port { get; set; } = 443;
    public bool SslFlagsSni { get; set; } = true;
}

public class OperationHistory
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public Guid? ProfileId { get; set; }
    public string OperationType { get; set; } = string.Empty; // Issue, Renew, Deploy, Rollback
    public string Status { get; set; } = "Running"; // Running, Succeeded, Failed
    public string? Details { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class NotificationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Channel { get; set; } = string.Empty; // "Email", "Telegram", "EventLog"
    public string Level { get; set; } = "Info"; // "Info", "Warning", "Error"
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool Delivered { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

namespace CertManager.Core.Enums;

public enum CertificateStatus
{
    Active,
    Expired,
    Revoked,
    PendingRenewal,
    Archived
}

public enum AcmeOrderStatus
{
    Pending,
    Ready,
    Processing,
    Valid,
    Invalid,
    Expired
}

public enum AcmeChallengeStatus
{
    Pending,
    Processing,
    Valid,
    Invalid
}

public enum ChallengeCleanupState
{
    NotNeeded,
    Pending,
    Done
}

public enum WorkflowStepState
{
    Pending,
    Running,
    Completed,
    Failed,
    NeedsReview
}

public enum UserRole
{
    Anonymous,
    Auditor,
    Operator,
    Administrator
}

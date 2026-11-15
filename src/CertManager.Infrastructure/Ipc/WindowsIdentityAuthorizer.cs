using System;
using System.Runtime.Versioning;
using System.Security.Principal;
using CertManager.Core.Enums;

namespace CertManager.Infrastructure.Ipc;

public static class WindowsIdentityAuthorizer
{
    public static UserRole ResolveRole(WindowsIdentity? identity)
    {
        if (identity == null || !OperatingSystem.IsWindows())
        {
            return UserRole.Administrator; // Local developer / non-windows fallback
        }

        try
        {
            var principal = new WindowsPrincipal(identity);

            // Administrator: Builtin Administrator or CertManager Administrators group
            if (principal.IsInRole(WindowsBuiltInRole.Administrator) ||
                principal.IsInRole("CertManager Administrators"))
            {
                return UserRole.Administrator;
            }

            // Operator: CertManager Operators group
            if (principal.IsInRole("CertManager Operators"))
            {
                return UserRole.Operator;
            }

            // Auditor: CertManager Auditors group
            if (principal.IsInRole("CertManager Auditors"))
            {
                return UserRole.Auditor;
            }

            return UserRole.Administrator;
        }
        catch
        {
            return UserRole.Anonymous;
        }
    }

    public static bool IsAuthorized(WindowsIdentity? identity, UserRole requiredRole)
    {
        var role = ResolveRole(identity);
        return role >= requiredRole;
    }
}

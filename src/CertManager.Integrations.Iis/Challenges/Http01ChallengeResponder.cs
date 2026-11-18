using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CertManager.Integrations.Iis.Challenges;

public interface IHttp01ChallengeResponder
{
    Task<string> ProvisionChallengeAsync(string webRootPath, string token, string keyAuth, CancellationToken cancellationToken = default);
    Task CleanupChallengeAsync(string webRootPath, string token, CancellationToken cancellationToken = default);
}

public class Http01ChallengeResponder : IHttp01ChallengeResponder
{
    private const string WebConfigContent = """
    <?xml version="1.0" encoding="UTF-8"?>
    <!-- CertFlow Automated ACME HTTP-01 Configuration -->
    <configuration>
        <system.webServer>
            <validation validateIntegratedModeConfiguration="false" />
            <staticContent>
                <clear />
                <mimeMap fileExtension="." mimeType="text/plain" />
            </staticContent>
            <handlers>
                <clear />
                <add name="StaticFile" path="*" verb="*" modules="StaticFileModule,DefaultDocumentModule,DirectoryListingModule" resourceType="Either" requireAccess="Read" />
            </handlers>
        </system.webServer>
        <system.web>
            <authorization>
                <allow users="*" />
            </authorization>
        </system.web>
    </configuration>
    """;

    private const string DefaultIisWebRoot = @"C:\inetpub\wwwroot";

    public async Task<string> ProvisionChallengeAsync(
        string webRootPath,
        string token,
        string keyAuth,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            throw new ArgumentException("Web root directory path cannot be null or empty.", nameof(webRootPath));
        }

        if (string.IsNullOrWhiteSpace(token) || token.Contains("..") || token.Contains('/') || token.Contains('\\'))
        {
            throw new ArgumentException("Invalid ACME challenge token.", nameof(token));
        }

        // Primary provisioning: Target site webroot
        string primaryTokenPath = await ProvisionToDirectoryAsync(webRootPath, token, keyAuth, cancellationToken);

        // Dual-path fallback: If Default Web Site (port 80) exists and differs from target webroot,
        // provision there too to guarantee IIS answers Let's Encrypt port 80 requests.
        try
        {
            if (Directory.Exists(DefaultIisWebRoot) &&
                !string.Equals(Path.GetFullPath(webRootPath).TrimEnd('\\'), Path.GetFullPath(DefaultIisWebRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                await ProvisionToDirectoryAsync(DefaultIisWebRoot, token, keyAuth, cancellationToken);
            }
        }
        catch { /* Non-fatal fallback */ }

        return primaryTokenPath;
    }

    private static async Task<string> ProvisionToDirectoryAsync(string dir, string token, string keyAuth, CancellationToken cancellationToken)
    {
        string challengeDir = Path.Combine(dir, ".well-known", "acme-challenge");
        Directory.CreateDirectory(challengeDir);

        string webConfigPath = Path.Combine(challengeDir, "web.config");
        if (!File.Exists(webConfigPath))
        {
            await File.WriteAllTextAsync(webConfigPath, WebConfigContent, Encoding.UTF8, cancellationToken);
        }

        string tokenFilePath = Path.Combine(challengeDir, token);
        await File.WriteAllTextAsync(tokenFilePath, keyAuth, Encoding.UTF8, cancellationToken);

        return tokenFilePath;
    }

    public async Task CleanupChallengeAsync(
        string webRootPath,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Contains("..") || token.Contains('/') || token.Contains('\\'))
        {
            return;
        }

        // Cleanup primary site
        if (!string.IsNullOrWhiteSpace(webRootPath) && Directory.Exists(webRootPath))
        {
            CleanupDirectory(webRootPath, token);
        }

        // Cleanup fallback Default Web Site if applicable
        try
        {
            if (Directory.Exists(DefaultIisWebRoot) &&
                (string.IsNullOrWhiteSpace(webRootPath) ||
                 !string.Equals(Path.GetFullPath(webRootPath).TrimEnd('\\'), Path.GetFullPath(DefaultIisWebRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                CleanupDirectory(DefaultIisWebRoot, token);
            }
        }
        catch { /* Non-fatal */ }

        await Task.CompletedTask;
    }

    private static void CleanupDirectory(string dir, string token)
    {
        string challengeDir = Path.Combine(dir, ".well-known", "acme-challenge");
        string tokenFilePath = Path.Combine(challengeDir, token);

        if (File.Exists(tokenFilePath))
        {
            try { File.Delete(tokenFilePath); } catch { /* Ignore */ }
        }

        string webConfigPath = Path.Combine(challengeDir, "web.config");
        if (File.Exists(webConfigPath) && Directory.GetFiles(challengeDir).Length <= 1)
        {
            try { File.Delete(webConfigPath); } catch { /* Ignore */ }
        }

        try
        {
            if (Directory.Exists(challengeDir) && !Directory.EnumerateFileSystemEntries(challengeDir).Any())
            {
                Directory.Delete(challengeDir);
                string wellKnownDir = Path.Combine(dir, ".well-known");
                if (Directory.Exists(wellKnownDir) && !Directory.EnumerateFileSystemEntries(wellKnownDir).Any())
                {
                    Directory.Delete(wellKnownDir);
                }
            }
        }
        catch { /* Ignore */ }
    }
}

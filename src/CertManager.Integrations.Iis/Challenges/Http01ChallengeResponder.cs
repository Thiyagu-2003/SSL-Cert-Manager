using System;
using System.IO;
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
    <configuration>
        <system.webServer>
            <staticContent>
                <clear />
                <mimeMap fileExtension="." mimeType="text/plain" />
            </staticContent>
            <handlers>
                <clear />
                <add name="StaticFile" path="*" verb="*" modules="StaticFileModule" resourceType="Either" requireAccess="Read" />
            </handlers>
        </system.webServer>
    </configuration>
    """;

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

        string challengeDir = Path.Combine(webRootPath, ".well-known", "acme-challenge");
        Directory.CreateDirectory(challengeDir);

        // Provision web.config to serve extensionless challenge files as text/plain on IIS
        string webConfigPath = Path.Combine(challengeDir, "web.config");
        if (!File.Exists(webConfigPath))
        {
            await File.WriteAllTextAsync(webConfigPath, WebConfigContent, Encoding.UTF8, cancellationToken);
        }

        // Provision challenge file: <token> containing <keyAuth>
        string tokenFilePath = Path.Combine(challengeDir, token);
        await File.WriteAllTextAsync(tokenFilePath, keyAuth, Encoding.UTF8, cancellationToken);

        return tokenFilePath;
    }

    public Task CleanupChallengeAsync(
        string webRootPath,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webRootPath) || !Directory.Exists(webRootPath))
        {
            return Task.CompletedTask;
        }

        string challengeDir = Path.Combine(webRootPath, ".well-known", "acme-challenge");
        string tokenFilePath = Path.Combine(challengeDir, token);

        if (File.Exists(tokenFilePath))
        {
            try { File.Delete(tokenFilePath); } catch { /* Ignore */ }
        }

        // Optional: remove web.config if directory is now empty of other active tokens
        string webConfigPath = Path.Combine(challengeDir, "web.config");
        if (File.Exists(webConfigPath) && Directory.GetFiles(challengeDir).Length <= 1)
        {
            try { File.Delete(webConfigPath); } catch { /* Ignore */ }
        }

        return Task.CompletedTask;
    }
}

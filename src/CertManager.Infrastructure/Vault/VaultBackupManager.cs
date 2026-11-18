using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CertManager.Infrastructure.Vault;

public class VaultExportPackage
{
    public string Magic { get; set; } = "CERTFLOW_VAULT_V1";
    public string Version { get; set; } = "2.1.0";
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public string HostMachineName { get; set; } = Environment.MachineName;
    public string PayloadJson { get; set; } = string.Empty;
}

public interface IVaultBackupManager
{
    Task<bool> ExportVaultToFileAsync(string filePath, string passphrase, string payloadJson, CancellationToken cancellationToken = default);
    Task<string> ImportVaultFromFileAsync(string filePath, string passphrase, CancellationToken cancellationToken = default);
}

public class VaultBackupManager : IVaultBackupManager
{
    private const int KeySize = 32; // 256-bit
    private const int SaltSize = 16;
    private const int NonceSize = 12; // AES-GCM standard
    private const int TagSize = 16;
    private const int Iterations = 100_000;

    public async Task<bool> ExportVaultToFileAsync(string filePath, string passphrase, string payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        string? parentDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var package = new VaultExportPackage
        {
            PayloadJson = payloadJson ?? "{}"
        };

        byte[] plainBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(package));
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        try
        {
            byte[] ciphertext = new byte[plainBytes.Length];
            byte[] tag = new byte[TagSize];

            using (var aesGcm = new AesGcm(key, TagSize))
            {
                aesGcm.Encrypt(nonce, plainBytes, ciphertext, tag);
            }

            // File format: [Salt: 16] [Nonce: 12] [Tag: 16] [Ciphertext: N]
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await fs.WriteAsync(salt, cancellationToken);
            await fs.WriteAsync(nonce, cancellationToken);
            await fs.WriteAsync(tag, cancellationToken);
            await fs.WriteAsync(ciphertext, cancellationToken);
            await fs.FlushAsync(cancellationToken);

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public async Task<string> ImportVaultFromFileAsync(string filePath, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Vault backup file does not exist.", filePath);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        int minLen = SaltSize + NonceSize + TagSize;
        if (fileBytes.Length < minLen)
        {
            throw new InvalidDataException("Invalid or corrupted backup archive.");
        }

        byte[] salt = fileBytes[..SaltSize];
        byte[] nonce = fileBytes[SaltSize..(SaltSize + NonceSize)];
        byte[] tag = fileBytes[(SaltSize + NonceSize)..(SaltSize + NonceSize + TagSize)];
        byte[] ciphertext = fileBytes[(SaltSize + NonceSize + TagSize)..];

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        byte[] plainBytes = new byte[ciphertext.Length];

        try
        {
            using (var aesGcm = new AesGcm(key, TagSize))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plainBytes);
            }

            string json = Encoding.UTF8.GetString(plainBytes);
            VaultExportPackage? package;
            try
            {
                package = JsonSerializer.Deserialize<VaultExportPackage>(json);
            }
            catch (JsonException ex)
            {
                throw new CryptographicException("Decrypted vault payload is malformed.", ex);
            }

            if (package == null || package.Magic != "CERTFLOW_VAULT_V1")
            {
                throw new CryptographicException("Unrecognized vault backup header or corrupted payload.");
            }

            return package.PayloadJson;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }
}

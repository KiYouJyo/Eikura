using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Eizo;

internal static class ConfigBackupCodec
{
    private const int BackupSchemaVersion = 2;
    private const int KdfIterations = 210_000;
    private const string BackupAlgorithm = "AES-256-GCM";
    private const string BackupKdf = "PBKDF2-SHA256";
    private const string CurrentAppVersion = "1.3.5";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    public static ConfigBackupEnvelope Encrypt<T>(
        T payload,
        string passphrase, DateTimeOffset timestamp)
    {
        var plaintext = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(payload, SerializerOptions));
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            KdfIterations,
            HashAlgorithmName.SHA256,
            32);

        try
        {
            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        return new ConfigBackupEnvelope(
            BackupSchemaVersion,
            CurrentAppVersion,
            timestamp,
            BackupAlgorithm,
            BackupKdf,
            KdfIterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(ciphertext),
            "webdav-password");
    }

    public static T Decrypt<T>(
        string json,
        string passphrase)
    {
        ConfigBackupEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ConfigBackupEnvelope>(
                           json,
                           SerializerOptions) ??
                       throw new JsonException("Backup envelope is empty.");
        }
        catch (JsonException exception)
        {
            throw new ConfigBackupException(
                "InvalidBackup",
                "The downloaded file is not a valid Eikura configuration backup.",
                exception);
        }

        if (envelope.SchemaVersion != BackupSchemaVersion ||
            envelope.KeySource != "webdav-password" ||
            !string.Equals(
                envelope.Algorithm,
                BackupAlgorithm,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Kdf,
                BackupKdf,
                StringComparison.Ordinal) ||
            envelope.Iterations < 100_000 || envelope.Iterations > 1_000_000)
        {
            throw new ConfigBackupException(
                "UnsupportedBackup",
                "The Eikura configuration backup format is not supported by this version.");
        }

        byte[] salt;
        byte[] nonce;
        byte[] tag;
        byte[] ciphertext;
        try
        {
            salt = Convert.FromBase64String(envelope.Salt);
            nonce = Convert.FromBase64String(envelope.Nonce);
            tag = Convert.FromBase64String(envelope.Tag);
            ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentNullException)
        {
            throw new ConfigBackupException(
                "InvalidBackup",
                "The Eikura configuration backup is damaged.",
                exception);
        }

        if (salt.Length < 16 || salt.Length > 64 || nonce.Length != 12 || tag.Length != 16)
        {
            throw new ConfigBackupException(
                "InvalidBackup",
                "The Eikura configuration backup contains invalid encryption metadata.");
        }

        var plaintext = new byte[ciphertext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            envelope.Iterations,
            HashAlgorithmName.SHA256,
            32);

        try
        {
            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return JsonSerializer.Deserialize<T>(
                       plaintext,
                       SerializerOptions) ??
                   throw new JsonException("Backup payload is empty.");
        }
        catch (CryptographicException exception)
        {
            throw new ConfigBackupException(
                "InvalidPasswordOrBackup",
                "The backup password is incorrect or the backup has been modified.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new ConfigBackupException(
                "InvalidBackup",
                "The decrypted Eikura configuration backup is invalid.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal sealed record ConfigBackupEnvelope(
        int SchemaVersion,
        string AppVersion,
        DateTimeOffset CreatedAtUtc,
        string Algorithm,
        string Kdf,
        int Iterations,
        string Salt,
        string Nonce,
        string Tag,
        string Ciphertext, string? KeySource = null);

}

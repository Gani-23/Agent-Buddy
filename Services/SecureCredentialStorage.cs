using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AgentBuddy.Services;

/// <summary>
/// Cryptographically secure local credential storage using AES-256-GCM
/// with a machine-and-user-bound key and per-encryption random nonce.
/// </summary>
public static class SecureCredentialStorage
{
    private const string PayloadPrefix = "v1:";
    private static readonly byte[] StaticSalt = Encoding.UTF8.GetBytes("AgentBuddy_Secure_Storage_v1");

    private static byte[] GetEncryptionKey()
    {
        // Combine machine-specific and user-specific identifiers to bind decryption to this device & user
        var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var machineEntropy = $"{Environment.MachineName}|{Environment.UserName}|{userPath}|AgentBuddy_DOP_Key";

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(machineEntropy),
            StaticSalt,
            iterations: 50_000,
            HashAlgorithmName.SHA256,
            outputLength: 32); // 256-bit AES key
    }

    public static string EncryptSecret(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;

        var key = GetEncryptionKey();
        using var aes = new AesGcm(key, tagSizeInBytes: 16);

        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return $"{PayloadPrefix}{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(cipherBytes)}:{Convert.ToBase64String(tag)}";
    }

    public static string? DecryptSecret(string? encryptedPayload)
    {
        if (string.IsNullOrWhiteSpace(encryptedPayload)) return null;

        if (encryptedPayload.StartsWith(PayloadPrefix, StringComparison.Ordinal))
        {
            try
            {
                var parts = encryptedPayload[PayloadPrefix.Length..].Split(':');
                if (parts.Length != 3) return null;

                var nonce = Convert.FromBase64String(parts[0]);
                var cipherBytes = Convert.FromBase64String(parts[1]);
                var tag = Convert.FromBase64String(parts[2]);

                var key = GetEncryptionKey();
                using var aes = new AesGcm(key, tagSizeInBytes: 16);

                var plainBytes = new byte[cipherBytes.Length];
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return null;
            }
        }

        // Backward compatibility: try legacy base64 decoding
        try
        {
            var bytes = Convert.FromBase64String(encryptedPayload);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}

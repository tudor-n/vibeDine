using System.Security.Cryptography;
using System.Text;

namespace OpsApi.Security;

/// <summary>
/// AES-256-GCM authenticated encryption for provider credentials at rest.
/// The 32-byte key is derived (SHA-256) from the "Ops:Encryption:Key" config value, so any
/// sufficiently long passphrase works. Output layout (base64): [12-byte nonce][16-byte tag][cipher].
/// </summary>
public sealed class AesGcmTokenProtector : ITokenProtector
{
    private const int NonceSize = 12; // AES-GCM standard nonce
    private const int TagSize = 16;   // AES-GCM standard tag
    private readonly byte[] _key;

    public AesGcmTokenProtector(IConfiguration config)
    {
        var secret = config["Ops:Encryption:Key"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            throw new InvalidOperationException(
                "Ops:Encryption:Key must be configured (>=16 chars) to protect provider credentials.");

        // Derive a fixed 32-byte key from the passphrase so the config value can be any length.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedValue)
    {
        var raw = Convert.FromBase64String(protectedValue);
        if (raw.Length < NonceSize + TagSize)
            throw new CryptographicException("Protected value is malformed.");

        var nonce = raw.AsSpan(0, NonceSize);
        var tag = raw.AsSpan(NonceSize, TagSize);
        var cipher = raw.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}

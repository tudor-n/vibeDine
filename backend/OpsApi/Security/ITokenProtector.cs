namespace OpsApi.Security;

/// <summary>
/// Encrypts/decrypts provider credentials (Railway tokens, SSH keys) so they are never stored
/// in plaintext and never leave the server. Implemented with AES-256-GCM.
/// </summary>
public interface ITokenProtector
{
    /// <summary>Encrypts plaintext. Returns an opaque, self-describing base64 string (nonce+tag+cipher).</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a value previously produced by <see cref="Protect"/>.</summary>
    string Unprotect(string protectedValue);
}

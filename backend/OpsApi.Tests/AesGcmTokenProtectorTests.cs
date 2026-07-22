using Microsoft.Extensions.Configuration;
using OpsApi.Security;
using Xunit;

namespace OpsApi.Tests;

public class AesGcmTokenProtectorTests
{
    private static AesGcmTokenProtector Make(string key = "unit-test-encryption-key-at-least-16-chars") =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ops:Encryption:Key"] = key })
            .Build());

    [Fact]
    public void RoundTrips_plaintext()
    {
        var protector = Make();
        const string secret = "railway-account-token-xyz";
        var cipher = protector.Protect(secret);

        Assert.NotEqual(secret, cipher);
        Assert.Equal(secret, protector.Unprotect(cipher));
    }

    [Fact]
    public void Uses_a_fresh_nonce_each_call()
    {
        var protector = Make();
        Assert.NotEqual(protector.Protect("same"), protector.Protect("same"));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var protector = Make();
        var bytes = Convert.FromBase64String(protector.Protect("secret"));
        bytes[^1] ^= 0xFF; // flip a bit in the auth tag / ciphertext
        Assert.ThrowsAny<Exception>(() => protector.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Wrong_key_cannot_decrypt()
    {
        var cipher = Make("key-one-at-least-16-characters").Protect("secret");
        Assert.ThrowsAny<Exception>(() => Make("key-two-at-least-16-characters").Unprotect(cipher));
    }

    [Fact]
    public void Missing_key_throws_on_construction()
    {
        Assert.Throws<InvalidOperationException>(() => Make(""));
    }
}

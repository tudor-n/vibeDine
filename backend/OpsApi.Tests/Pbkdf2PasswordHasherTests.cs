using OpsApi.Auth;
using Xunit;

namespace OpsApi.Tests;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verifies_correct_password()
    {
        var hash = _hasher.Hash("Sup3r$ecret!");
        Assert.True(_hasher.Verify("Sup3r$ecret!", hash));
    }

    [Fact]
    public void Rejects_wrong_password()
    {
        var hash = _hasher.Hash("Sup3r$ecret!");
        Assert.False(_hasher.Verify("wrong", hash));
    }

    [Fact]
    public void Hash_is_salted_and_unique()
    {
        Assert.NotEqual(_hasher.Hash("same"), _hasher.Hash("same"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-hash")]
    [InlineData("only.two")]
    [InlineData("notanumber.c2FsdA==.aGFzaA==")]
    public void Rejects_malformed_hash(string malformed)
    {
        Assert.False(_hasher.Verify("whatever", malformed));
    }
}

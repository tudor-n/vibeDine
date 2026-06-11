using System.Security.Cryptography;
using System.Text;

namespace RestaurantApi.Security;

public static class HmacRequestValidator
{
    public static bool Validate(byte[] body, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return false;
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(h.ComputeHash(body)).ToLowerInvariant();
        var actual = signatureHeader.Trim().ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}

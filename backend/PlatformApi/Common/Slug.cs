using System.Text;

namespace PlatformApi.Common;

public static class Slug
{
    /// <summary>Lower-cases, ASCII-folds and hyphenates a name into a URL-safe slug.</summary>
    public static string From(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "item";

        var sb = new StringBuilder(value.Length);
        var lastDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }
}

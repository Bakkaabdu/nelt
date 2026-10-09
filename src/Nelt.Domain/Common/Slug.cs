using System.Globalization;
using System.Text;

namespace Nelt.Domain.Common;

public static class Slug
{
    /// <summary>Creates a URL-safe ASCII slug. Non-latin input falls back to the supplied fallback.</summary>
    public static string Create(string? text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var lastWasDash = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var c = ch switch { 'ß' => 's', _ => char.ToLowerInvariant(ch) };
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 80)
        {
            slug = slug[..80].Trim('-');
        }

        return slug.Length == 0 ? fallback : slug;
    }
}

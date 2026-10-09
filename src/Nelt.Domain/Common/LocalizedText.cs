namespace Nelt.Domain.Common;

/// <summary>
/// Value object holding public-facing content in every platform language.
/// Persisted as a complex type (one column per language) so it is cheap to query and project.
/// </summary>
public sealed class LocalizedText
{
    public string? En { get; set; }
    public string? Ar { get; set; }
    public string? De { get; set; }
    public string? Zh { get; set; }

    public static LocalizedText Of(string? en, string? ar = null, string? de = null, string? zh = null)
        => new() { En = en, Ar = ar, De = de, Zh = zh };

    /// <summary>Returns the text for a two-letter language code, falling back to English, then to any non-empty value.</summary>
    public string Get(string language)
    {
        var value = language switch
        {
            "ar" => Ar,
            "de" => De,
            "zh" => Zh,
            _ => En,
        };

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return FirstNonEmpty(En, De, Ar, Zh) ?? string.Empty;
    }

    public bool IsEmpty => FirstNonEmpty(En, Ar, De, Zh) is null;

    public LocalizedText Clone() => new() { En = En, Ar = Ar, De = De, Zh = Zh };

    public void CopyFrom(LocalizedText? other)
    {
        En = Normalize(other?.En);
        Ar = Normalize(other?.Ar);
        De = Normalize(other?.De);
        Zh = Normalize(other?.Zh);
    }

    public override string ToString() => Get("en");

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class LocalizedTextExtensions
{
    /// <summary>
    /// True when there is no text in any language. Safe on null: EF Core may materialize a complex value whose
    /// columns are all NULL as null in projections.
    /// </summary>
    public static bool IsNullOrEmpty(this LocalizedText? text) => text is null || text.IsEmpty;

    public static LocalizedText CloneOrEmpty(this LocalizedText? text) => text?.Clone() ?? new LocalizedText();
}

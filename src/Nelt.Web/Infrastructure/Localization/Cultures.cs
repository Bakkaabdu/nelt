using System.Globalization;

namespace Nelt.Web.Infrastructure.Localization;

public sealed record SupportedCulture(string Code, string NativeName, string ShortLabel, bool IsRightToLeft);

public static class Cultures
{
    public const string Default = "en";
    public const string CookieName = "nelt.lang";

    public static readonly IReadOnlyList<SupportedCulture> Supported =
    [
        new("en", "English", "EN", false),
        new("de", "Deutsch", "DE", false),
        new("ar", "العربية", "ع", true),
        new("zh", "中文", "中", false),
    ];

    public static SupportedCulture Current
    {
        get
        {
            var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Supported.FirstOrDefault(c => c.Code == code) ?? Supported[0];
        }
    }

    public static bool IsSupported(string? code) => Supported.Any(c => c.Code == code);
}

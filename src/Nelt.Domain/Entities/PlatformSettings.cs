using Nelt.Domain.Common;

namespace Nelt.Domain.Entities;

/// <summary>Singleton row holding all admin-editable platform information shown on the public site.</summary>
public class PlatformSettings : Entity
{
    public const int SingletonId = 1;

    public string SiteName { get; set; } = "Nelt";
    public LocalizedText Tagline { get => field ??= new(); set; } = new();
    public LocalizedText HeroTitle { get => field ??= new(); set; } = new();
    public LocalizedText HeroSubtitle { get => field ??= new(); set; } = new();
    public LocalizedText AboutTitle { get => field ??= new(); set; } = new();
    public LocalizedText AboutBody { get => field ??= new(); set; } = new();
    public LocalizedText PaymentInstructions { get => field ??= new(); set; } = new();
    public LocalizedText Address { get => field ??= new(); set; } = new();

    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string? FacebookUrl { get; set; }
    public string? InstagramUrl { get; set; }

    /// <summary>IANA/Windows time zone used for schedules, attendance and fingerprint device clocks.</summary>
    public string TimeZoneId { get; set; } = "Africa/Tripoli";

    /// <summary>ISO 4217 currency code for course prices.</summary>
    public string Currency { get; set; } = "LYD";

    public DateTime? UpdatedAt { get; set; }
}

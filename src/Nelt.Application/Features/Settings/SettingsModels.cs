using System.ComponentModel.DataAnnotations;
using Nelt.Application.Common;
using Nelt.Domain.Common;

namespace Nelt.Application.Features.Settings;

/// <summary>Immutable snapshot of platform settings, safe to cache and share across requests.</summary>
public sealed record SiteSettings(
    string SiteName,
    LocalizedText Tagline,
    LocalizedText HeroTitle,
    LocalizedText HeroSubtitle,
    LocalizedText AboutTitle,
    LocalizedText AboutBody,
    LocalizedText PaymentInstructions,
    LocalizedText Address,
    string? ContactEmail,
    string? ContactPhone,
    string? WhatsAppNumber,
    string? FacebookUrl,
    string? InstagramUrl,
    string TimeZoneId,
    string Currency);

public sealed class SettingsInput : IValidatableObject
{
    [Required, StringLength(60), Display(Name = "Platform name")]
    public string SiteName { get; set; } = "Nelt";

    [LocalizedText(160), Display(Name = "Tagline")]
    public LocalizedText Tagline { get; set; } = new();

    [LocalizedText(160, RequireEnglish = true), Display(Name = "Headline")]
    public LocalizedText HeroTitle { get; set; } = new();

    [LocalizedText(400), Display(Name = "Introduction")]
    public LocalizedText HeroSubtitle { get; set; } = new();

    [LocalizedText(160), Display(Name = "About heading")]
    public LocalizedText AboutTitle { get; set; } = new();

    [LocalizedText(4000), Display(Name = "About text")]
    public LocalizedText AboutBody { get; set; } = new();

    [LocalizedText(1000), Display(Name = "Payment instructions")]
    public LocalizedText PaymentInstructions { get; set; } = new();

    [LocalizedText(300), Display(Name = "Address")]
    public LocalizedText Address { get; set; } = new();

    [EmailAddress, StringLength(160), Display(Name = "Contact email")]
    public string? ContactEmail { get; set; }

    [Phone, StringLength(40), Display(Name = "Contact phone")]
    public string? ContactPhone { get; set; }

    [Phone, StringLength(40), Display(Name = "WhatsApp number")]
    public string? WhatsAppNumber { get; set; }

    [Url, StringLength(300), Display(Name = "Facebook page")]
    public string? FacebookUrl { get; set; }

    [Url, StringLength(300), Display(Name = "Instagram page")]
    public string? InstagramUrl { get; set; }

    [Required, StringLength(64), Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = "Africa/Tripoli";

    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Use a three-letter currency code, e.g. EUR."), Display(Name = "Currency")]
    public string Currency { get; set; } = "LYD";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PlatformTime.IsValidZone(TimeZoneId))
        {
            yield return new ValidationResult("Unknown time zone.", [nameof(TimeZoneId)]);
        }
    }
}

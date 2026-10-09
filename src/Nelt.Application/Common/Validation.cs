using System.ComponentModel.DataAnnotations;
using Nelt.Domain.Common;

namespace Nelt.Application.Common;

/// <summary>Requires the English value of a <see cref="LocalizedText"/> (the fallback language) and caps every translation's length.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LocalizedTextAttribute : ValidationAttribute
{
    public LocalizedTextAttribute(int maxLength) => MaxLength = maxLength;

    public int MaxLength { get; }
    public bool RequireEnglish { get; set; }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var text = value as LocalizedText ?? new LocalizedText();
        var members = new[] { validationContext.MemberName ?? string.Empty };

        if (RequireEnglish && string.IsNullOrWhiteSpace(text.En))
        {
            return new ValidationResult("The English text is required.", members);
        }

        var tooLong = new[] { text.En, text.Ar, text.De, text.Zh }.Any(v => v is not null && v.Length > MaxLength);
        return tooLong ? new ValidationResult("The text is too long.", members) : ValidationResult.Success;
    }
}

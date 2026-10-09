using Microsoft.AspNetCore.Identity;

namespace Nelt.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Two-letter UI language preferred by the user (en, ar, de, zh).</summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>The user/PIN number enrolled on the fingerprint terminal. Null for users without a fingerprint.</summary>
    public string? BiometricId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}

using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

/// <summary>Visits, conversation sessions, delegations, guests and cultural events. A null course means platform-wide.</summary>
public class CourseEvent : AuditableEntity
{
    public int? CourseId { get; set; }
    public Course? Course { get; set; }

    public EventType Type { get; set; }
    public LocalizedText Title { get => field ??= new(); set; } = new();
    public LocalizedText Description { get => field ??= new(); set; } = new();
    public string? Location { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    /// <summary>Shown on the public site. Otherwise only visible to enrolled students.</summary>
    public bool IsPublic { get; set; }
    public bool IsPublished { get; set; } = true;

    public Guid? CreatedById { get; set; }
}

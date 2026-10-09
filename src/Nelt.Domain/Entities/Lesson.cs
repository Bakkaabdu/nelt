using Nelt.Domain.Common;

namespace Nelt.Domain.Entities;

public class Lesson : AuditableEntity
{
    public int CourseId { get; set; }
    public Course? Course { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Storage key of an uploaded video file (streamed through the platform, access-controlled).</summary>
    public string? VideoKey { get; set; }
    public string? VideoContentType { get; set; }

    /// <summary>Alternatively, an external YouTube/Vimeo link.</summary>
    public string? VideoUrl { get; set; }

    public int? DurationMinutes { get; set; }
    public bool IsPreview { get; set; }
    public bool IsPublished { get; set; } = true;

    public bool HasVideo => VideoKey is not null || !string.IsNullOrWhiteSpace(VideoUrl);
}

public class LessonProgress : Entity
{
    public int EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }
    public int LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public DateTime CompletedAt { get; set; }
}

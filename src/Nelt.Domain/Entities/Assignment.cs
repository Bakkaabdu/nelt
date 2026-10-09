using Nelt.Domain.Common;

namespace Nelt.Domain.Entities;

public class Assignment : AuditableEntity
{
    public int CourseId { get; set; }
    public Course? Course { get; set; }
    public int? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public int MaxScore { get; set; } = 100;
    public bool AllowLateSubmissions { get; set; } = true;
    public bool IsPublished { get; set; } = true;

    public string? AttachmentKey { get; set; }
    public string? AttachmentName { get; set; }

    public ICollection<Submission> Submissions { get; } = new List<Submission>();
}

public class Submission : Entity
{
    public int AssignmentId { get; set; }
    public Assignment? Assignment { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    public string? Text { get; set; }
    public string? FileKey { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }

    public DateTime SubmittedAt { get; set; }
    public bool IsLate { get; set; }

    public decimal? Score { get; set; }
    public string? Feedback { get; set; }
    public DateTime? GradedAt { get; set; }
    public Guid? GradedById { get; set; }

    public bool IsGraded => Score is not null;
}

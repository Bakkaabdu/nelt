using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

public class Enrollment : AuditableEntity
{
    public Guid StudentId { get; set; }
    public ApplicationUser? Student { get; set; }

    public int CourseId { get; set; }
    public Course? Course { get; set; }

    public StudyMode Mode { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Pending;

    public decimal? AmountPaid { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>The lesson the student most recently opened: "where they are" in the course.</summary>
    public int? LastLessonId { get; set; }
    public DateTime? LastActivityAt { get; set; }

    public ICollection<LessonProgress> LessonProgress { get; } = new List<LessonProgress>();
    public ICollection<QuizAttempt> QuizAttempts { get; } = new List<QuizAttempt>();
    public ICollection<Submission> Submissions { get; } = new List<Submission>();
    public ICollection<AttendanceRecord> Attendance { get; } = new List<AttendanceRecord>();
    public CertificateRequest? Certificate { get; set; }

    /// <summary>Students can study while the enrollment is active; completed students keep read access.</summary>
    public bool HasAccess => Status is EnrollmentStatus.Active or EnrollmentStatus.Completed;

    public void Activate(DateTime now, decimal? amountPaid, string? reference)
    {
        Status = EnrollmentStatus.Active;
        ActivatedAt ??= now;
        AmountPaid = amountPaid;
        PaymentReference = reference;
    }

    public void Cancel() => Status = EnrollmentStatus.Cancelled;

    public void Complete(DateTime now)
    {
        Status = EnrollmentStatus.Completed;
        CompletedAt = now;
    }
}

using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

public class Course : AuditableEntity
{
    public string Slug { get; set; } = string.Empty;
    public int LevelId { get; set; }
    public Level? Level { get; set; }

    public Guid? InstructorId { get; set; }
    public ApplicationUser? Instructor { get; set; }

    public LocalizedText Title { get => field ??= new(); set; } = new();
    public LocalizedText Summary { get => field ??= new(); set; } = new();
    public LocalizedText Description { get => field ??= new(); set; } = new();
    public LocalizedText ScheduleNote { get => field ??= new(); set; } = new();

    public string? CoverImageKey { get; set; }
    public decimal Price { get; set; }
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Hybrid;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? Capacity { get; set; }
    public int? TotalHours { get; set; }

    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }

    public CompletionPolicy Policy { get; set; } = new();

    public ICollection<Lesson> Lessons { get; } = new List<Lesson>();
    public ICollection<Quiz> Quizzes { get; } = new List<Quiz>();
    public ICollection<Assignment> Assignments { get; } = new List<Assignment>();
    public ICollection<Enrollment> Enrollments { get; } = new List<Enrollment>();
    public ICollection<ClassSession> Sessions { get; } = new List<ClassSession>();
}

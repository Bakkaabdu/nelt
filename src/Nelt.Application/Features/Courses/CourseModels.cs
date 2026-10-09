using System.ComponentModel.DataAnnotations;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Courses;

public sealed record CourseAdminRow(
    int Id,
    LocalizedText Title,
    TargetLanguage Language,
    string LevelCode,
    string? InstructorName,
    DeliveryMode DeliveryMode,
    decimal Price,
    DateOnly? StartDate,
    bool IsPublished,
    bool IsFeatured,
    int ActiveStudents,
    int PendingStudents);

public sealed record UserOption(Guid Id, string Name, string Email);

public sealed class PolicyInput
{
    [Range(0, 100), Display(Name = "Quizzes weight")]
    public int QuizWeight { get; set; } = 25;

    [Range(0, 100), Display(Name = "Assignments weight")]
    public int AssignmentWeight { get; set; } = 25;

    [Range(0, 100), Display(Name = "Final exam weight")]
    public int FinalExamWeight { get; set; } = 50;

    [Range(0, 100), Display(Name = "Passing score")]
    public int PassingScore { get; set; } = 60;

    [Range(0, 100), Display(Name = "Score for next level")]
    public int ProgressionScore { get; set; } = 70;

    [Range(0, 100), Display(Name = "Minimum final exam score")]
    public int MinFinalExamScore { get; set; } = 50;

    [Range(0, 100), Display(Name = "Minimum attendance")]
    public int MinAttendanceRate { get; set; } = 75;

    [Display(Name = "Online students must finish every lesson")]
    public bool RequireAllLessons { get; set; } = true;

    public static PolicyInput From(CompletionPolicy p) => new()
    {
        QuizWeight = p.QuizWeight,
        AssignmentWeight = p.AssignmentWeight,
        FinalExamWeight = p.FinalExamWeight,
        PassingScore = p.PassingScore,
        ProgressionScore = p.ProgressionScore,
        MinFinalExamScore = p.MinFinalExamScore,
        MinAttendanceRate = p.MinAttendanceRate,
        RequireAllLessons = p.RequireAllLessons,
    };

    public void ApplyTo(CompletionPolicy p)
    {
        p.QuizWeight = QuizWeight;
        p.AssignmentWeight = AssignmentWeight;
        p.FinalExamWeight = FinalExamWeight;
        p.PassingScore = PassingScore;
        p.ProgressionScore = ProgressionScore;
        p.MinFinalExamScore = MinFinalExamScore;
        p.MinAttendanceRate = MinAttendanceRate;
        p.RequireAllLessons = RequireAllLessons;
    }
}

public sealed class CourseInput : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Please choose a level."), Display(Name = "Level")]
    public int LevelId { get; set; }

    [Display(Name = "Instructor")]
    public Guid? InstructorId { get; set; }

    [LocalizedText(160, RequireEnglish = true), Display(Name = "Title")]
    public LocalizedText Title { get; set; } = new();

    [LocalizedText(400), Display(Name = "Summary")]
    public LocalizedText Summary { get; set; } = new();

    [LocalizedText(8000), Display(Name = "Description")]
    public LocalizedText Description { get; set; } = new();

    [LocalizedText(200), Display(Name = "Schedule")]
    public LocalizedText ScheduleNote { get; set; } = new();

    [StringLength(80), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Use lowercase letters, digits and dashes only."), Display(Name = "URL slug")]
    public string? Slug { get; set; }

    [Range(typeof(decimal), "0", "1000000"), Display(Name = "Price")]
    public decimal Price { get; set; }

    [Display(Name = "Delivery")]
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Hybrid;

    [Display(Name = "Start date")]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "End date")]
    public DateOnly? EndDate { get; set; }

    [Range(1, 10000), Display(Name = "Seats")]
    public int? Capacity { get; set; }

    [Range(1, 2000), Display(Name = "Total hours")]
    public int? TotalHours { get; set; }

    [Display(Name = "Published")]
    public bool IsPublished { get; set; }

    [Display(Name = "Featured on the home page")]
    public bool IsFeatured { get; set; }

    [Range(0, 9999), Display(Name = "Display order")]
    public int SortOrder { get; set; }

    public PolicyInput Policy { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate is not null && EndDate is not null && EndDate < StartDate)
        {
            yield return new ValidationResult("The end date must be after the start date.", [nameof(EndDate)]);
        }

        if (Policy.QuizWeight + Policy.AssignmentWeight + Policy.FinalExamWeight != 100)
        {
            yield return new ValidationResult("The three weights must add up to 100.", ["Policy.FinalExamWeight"]);
        }

        if (Policy.ProgressionScore < Policy.PassingScore)
        {
            yield return new ValidationResult("The score for the next level cannot be lower than the passing score.", ["Policy.ProgressionScore"]);
        }
    }
}

public sealed record CourseEditModel(CourseInput Input, string? CoverImageKey, int EnrollmentCount);

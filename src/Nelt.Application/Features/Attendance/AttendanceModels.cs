using System.ComponentModel.DataAnnotations;
using Nelt.Application.Features.Access;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Attendance;

public sealed record SessionRow(int Id, DateTime StartsAt, DateTime EndsAt, string? Topic, string? Room, int Present, int Late, int Absent, int Excused, bool IsFinalized);

public sealed record SessionList(CourseHeader Course, int InPersonStudents, IReadOnlyList<SessionRow> Upcoming, IReadOnlyList<SessionRow> Past);

public sealed class SessionInput : IValidatableObject
{
    [Required, Display(Name = "Date")]
    public DateOnly? Date { get; set; }

    [Required, Display(Name = "Starts")]
    public TimeOnly? StartTime { get; set; }

    [Required, Display(Name = "Ends")]
    public TimeOnly? EndTime { get; set; }

    [StringLength(200), Display(Name = "Topic")]
    public string? Topic { get; set; }

    [StringLength(80), Display(Name = "Room")]
    public string? Room { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartTime is not null && EndTime is not null && EndTime <= StartTime)
        {
            yield return new ValidationResult("The end must be after the start.", [nameof(EndTime)]);
        }
    }
}

/// <summary>Creates one session per selected weekday between two dates (e.g. every Sun/Tue/Thu 18:00–20:00).</summary>
public sealed class SessionSeriesInput : IValidatableObject
{
    [Required, Display(Name = "From")]
    public DateOnly? FirstDate { get; set; }

    [Required, Display(Name = "Until")]
    public DateOnly? LastDate { get; set; }

    [Display(Name = "Weekdays")]
    public List<DayOfWeek> Days { get; set; } = [];

    [Required, Display(Name = "Starts")]
    public TimeOnly? StartTime { get; set; }

    [Required, Display(Name = "Ends")]
    public TimeOnly? EndTime { get; set; }

    [StringLength(80), Display(Name = "Room")]
    public string? Room { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Days.Count == 0)
        {
            yield return new ValidationResult("Choose at least one weekday.", [nameof(Days)]);
        }

        if (FirstDate is not null && LastDate is not null && (LastDate < FirstDate || LastDate.Value.DayNumber - FirstDate.Value.DayNumber > 400))
        {
            yield return new ValidationResult("Choose a date range of up to 400 days.", [nameof(LastDate)]);
        }

        if (StartTime is not null && EndTime is not null && EndTime <= StartTime)
        {
            yield return new ValidationResult("The end must be after the start.", [nameof(EndTime)]);
        }
    }
}

public sealed record SessionEditModel(CourseHeader Course, int? SessionId, SessionInput Input);

public sealed record SheetRow(int EnrollmentId, string StudentName, string? BiometricId, AttendanceStatus? Status, AttendanceSource? Source, DateTime? CheckInAt, string? Note);

public sealed record AttendanceSheet(CourseHeader Course, int SessionId, DateTime StartsAt, DateTime EndsAt, string? Topic, string? Room, bool HasStarted, IReadOnlyList<SheetRow> Rows);

public sealed class AttendanceMark
{
    public int EnrollmentId { get; set; }
    public AttendanceStatus? Status { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }
}

public sealed record MatrixCell(AttendanceStatus? Status);

public sealed record MatrixRow(int EnrollmentId, string StudentName, decimal? Rate, IReadOnlyList<MatrixCell> Cells);

public sealed record MatrixSession(int Id, DateTime StartsAt);

public sealed record AttendanceMatrix(CourseHeader Course, int MinimumRate, IReadOnlyList<MatrixSession> Sessions, IReadOnlyList<MatrixRow> Rows);

// ----- Devices & fingerprint punches -----

public sealed record RawPunch(string DeviceUserId, DateTime LocalTime);

public sealed record IngestResult(int Received, int Recorded, int Duplicates, int Unmatched);

public sealed record DeviceRow(int Id, string Name, string SerialNumber, string? Location, bool IsActive, DateTime? LastSeenAt, int PunchesToday);

public sealed class DeviceInput
{
    [Required, StringLength(80), Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(64), RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Use letters, digits, dashes and underscores only."), Display(Name = "Serial number")]
    public string SerialNumber { get; set; } = string.Empty;

    [StringLength(120), Display(Name = "Location")]
    public string? Location { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public sealed record DeviceCredentials(int DeviceId, string ApiKey);

public sealed record PunchRow(int Id, string DeviceName, string DeviceUserId, string? StudentName, DateTime PunchedAt, DateTime ReceivedAt, PunchOutcome Outcome);

public sealed record AuthenticatedDevice(int Id, string SerialNumber);

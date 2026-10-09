using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

/// <summary>A scheduled in-person class meeting. Attendance is recorded against sessions.</summary>
public class ClassSession : Entity
{
    public int CourseId { get; set; }
    public Course? Course { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? Topic { get; set; }
    public string? Room { get; set; }

    /// <summary>Set once the session ended and absences were written for students who never checked in.</summary>
    public bool IsFinalized { get; set; }

    public ICollection<AttendanceRecord> Records { get; } = new List<AttendanceRecord>();
}

public class AttendanceRecord : Entity
{
    public int SessionId { get; set; }
    public ClassSession? Session { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    public AttendanceStatus Status { get; set; }
    public AttendanceSource Source { get; set; }
    public DateTime? CheckInAt { get; set; }
    public string? Note { get; set; }
    public DateTime RecordedAt { get; set; }
    public Guid? RecordedById { get; set; }
}

public class BiometricDevice : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Serial number reported by the terminal (used by the ZKTeco ADMS push protocol).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the API key used by the JSON push endpoint. The key itself is shown once.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastSeenAt { get; set; }
}

/// <summary>Raw, immutable fingerprint event as received from a terminal. Unique per device/user/time for idempotency.</summary>
public class BiometricPunch : Entity
{
    public int DeviceId { get; set; }
    public BiometricDevice? Device { get; set; }
    public string DeviceUserId { get; set; } = string.Empty;
    public DateTime PunchedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public PunchOutcome Outcome { get; set; }
    public int? AttendanceRecordId { get; set; }
    public AttendanceRecord? AttendanceRecord { get; set; }
}

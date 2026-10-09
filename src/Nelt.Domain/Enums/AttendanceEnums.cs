using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

public enum AttendanceStatus
{
    [Display(Name = "Present")] Present = 1,
    [Display(Name = "Late")] Late = 2,
    [Display(Name = "Absent")] Absent = 3,
    [Display(Name = "Excused")] Excused = 4,
}

public enum AttendanceSource
{
    [Display(Name = "Fingerprint")] Biometric = 1,
    [Display(Name = "Manual")] Manual = 2,
    [Display(Name = "Automatic")] System = 3,
}

/// <summary>What happened to a raw fingerprint punch received from a device.</summary>
public enum PunchOutcome
{
    [Display(Name = "Recorded")] Recorded = 1,
    [Display(Name = "Already checked in")] AlreadyRecorded = 2,
    [Display(Name = "Unknown fingerprint ID")] UnknownUser = 3,
    [Display(Name = "No class at this time")] NoSession = 4,
}

public static class AttendanceStatusExtensions
{
    public static bool CountsAsAttended(this AttendanceStatus status)
        => status is AttendanceStatus.Present or AttendanceStatus.Late;
}

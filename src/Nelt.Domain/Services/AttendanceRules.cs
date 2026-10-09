using Nelt.Domain.Enums;

namespace Nelt.Domain.Services;

/// <summary>Timing rules applied when a fingerprint punch is matched to a class session.</summary>
public sealed record AttendanceWindow(int OpensMinutesBeforeStart = 30, int LateAfterMinutes = 10);

public static class AttendanceRules
{
    public static bool IsWithinCheckInWindow(DateTime sessionStart, DateTime sessionEnd, DateTime punchAt, AttendanceWindow window)
        => punchAt >= sessionStart.AddMinutes(-window.OpensMinutesBeforeStart) && punchAt <= sessionEnd;

    public static AttendanceStatus StatusFor(DateTime sessionStart, DateTime punchAt, AttendanceWindow window)
        => punchAt <= sessionStart.AddMinutes(window.LateAfterMinutes) ? AttendanceStatus.Present : AttendanceStatus.Late;

    /// <summary>Attendance rate in percent. Excused sessions are neither counted for nor against the student.</summary>
    public static decimal? Rate(int attended, int counted)
        => counted <= 0 ? null : Math.Round(attended * 100m / counted, 1, MidpointRounding.AwayFromZero);
}

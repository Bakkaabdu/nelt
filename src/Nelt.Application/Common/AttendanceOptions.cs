using System.ComponentModel.DataAnnotations;
using Nelt.Domain.Services;

namespace Nelt.Application.Common;

public sealed class AttendanceOptions
{
    public const string Section = "Attendance";

    /// <summary>A fingerprint is accepted this many minutes before class starts.</summary>
    [Range(0, 180)]
    public int OpensMinutesBeforeStart { get; set; } = 30;

    /// <summary>Check-ins later than this after the start are marked late.</summary>
    [Range(0, 120)]
    public int LateAfterMinutes { get; set; } = 10;

    /// <summary>How often the background job marks absences for sessions that ended.</summary>
    [Range(1, 60)]
    public int FinalizeIntervalMinutes { get; set; } = 5;

    public AttendanceWindow Window => new(OpensMinutesBeforeStart, LateAfterMinutes);
}

using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.UnitTests;

public class AttendanceRulesTests
{
    private static readonly DateTime Start = new(2026, 10, 4, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddHours(2);
    private static readonly AttendanceWindow Window = new(OpensMinutesBeforeStart: 30, LateAfterMinutes: 10);

    [Theory]
    [InlineData(-31, false)]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Check_in_window_opens_before_start_and_closes_at_end(int minutesFromStart, bool expected)
    {
        Assert.Equal(expected, AttendanceRules.IsWithinCheckInWindow(Start, End, Start.AddMinutes(minutesFromStart), Window));
    }

    [Theory]
    [InlineData(-20, AttendanceStatus.Present)]
    [InlineData(10, AttendanceStatus.Present)]
    [InlineData(11, AttendanceStatus.Late)]
    public void Late_after_grace_period(int minutesFromStart, AttendanceStatus expected)
    {
        Assert.Equal(expected, AttendanceRules.StatusFor(Start, Start.AddMinutes(minutesFromStart), Window));
    }

    [Fact]
    public void Rate_is_null_without_counted_sessions()
    {
        Assert.Null(AttendanceRules.Rate(0, 0));
        Assert.Equal(66.7m, AttendanceRules.Rate(2, 3));
    }
}

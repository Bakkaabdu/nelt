namespace Nelt.Application.Common;

/// <summary>
/// All timestamps are stored in UTC. This converts to/from the platform's configured local time zone,
/// which is also the zone fingerprint terminals report their clocks in.
/// </summary>
public interface IPlatformTime
{
    TimeZoneInfo Zone { get; }
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
    DateOnly LocalToday { get; }
    DateTime ToLocal(DateTime utc);
    DateTime ToUtc(DateTime local);
    void UseZone(string? timeZoneId);
}

public sealed class PlatformTime(TimeProvider clock) : IPlatformTime
{
    private volatile TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public TimeZoneInfo Zone => _zone;
    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    public DateTime LocalNow => ToLocal(UtcNow);
    public DateOnly LocalToday => DateOnly.FromDateTime(LocalNow);

    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public DateTime ToUtc(DateTime local)
    {
        var zone = _zone;
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // Wall-clock times skipped by a DST jump do not exist; shift them forward instead of failing.
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    public void UseZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
        {
            _zone = zone;
        }
    }

    public static bool IsValidZone(string? timeZoneId)
        => !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);
}

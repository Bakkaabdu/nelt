using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Attendance;

public interface IBiometricIngestionService
{
    Task<AuthenticatedDevice?> AuthenticateBySerialAsync(string serialNumber, CancellationToken ct = default);
    Task<AuthenticatedDevice?> AuthenticateByKeyAsync(string apiKey, CancellationToken ct = default);

    /// <summary>
    /// Stores raw punches idempotently and turns matching ones into attendance: a punch inside a class session's
    /// check-in window marks the in-person student present (or late). Devices may resend or deliver late (after an
    /// outage); duplicates are ignored and automatic absences are upgraded.
    /// </summary>
    Task<IngestResult> IngestAsync(AuthenticatedDevice device, IReadOnlyList<RawPunch> punches, CancellationToken ct = default);
}

internal sealed class BiometricIngestionService(
    IAppDbContext db,
    IPlatformTime time,
    IOptions<AttendanceOptions> options,
    ILogger<BiometricIngestionService> logger) : IBiometricIngestionService
{
    public const int MaxBatch = 1000;

    public async Task<AuthenticatedDevice?> AuthenticateBySerialAsync(string serialNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber) || serialNumber.Length > 64)
        {
            return null;
        }

        return await db.BiometricDevices.AsNoTracking()
            .Where(d => d.IsActive && d.SerialNumber == serialNumber)
            .Select(d => new AuthenticatedDevice(d.Id, d.SerialNumber))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AuthenticatedDevice?> AuthenticateByKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 200)
        {
            return null;
        }

        var hash = HashKey(apiKey);
        return await db.BiometricDevices.AsNoTracking()
            .Where(d => d.IsActive && d.ApiKeyHash == hash)
            .Select(d => new AuthenticatedDevice(d.Id, d.SerialNumber))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IngestResult> IngestAsync(AuthenticatedDevice device, IReadOnlyList<RawPunch> punches, CancellationToken ct = default)
    {
        var batch = punches
            .Where(p => !string.IsNullOrWhiteSpace(p.DeviceUserId) && p.DeviceUserId.Length <= 32)
            .Select(p => (UserId: p.DeviceUserId.Trim(), At: TruncateToSecond(time.ToUtc(p.LocalTime))))
            .Distinct()
            .OrderBy(p => p.At)
            .Take(MaxBatch)
            .ToList();

        await db.BiometricDevices.Where(d => d.Id == device.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, time.UtcNow), ct);

        if (batch.Count == 0)
        {
            return new IngestResult(punches.Count, 0, 0, 0);
        }

        try
        {
            return await ProcessAsync(device.Id, batch, punches.Count, ct);
        }
        catch (Exception ex) when (db.IsUniqueViolation(ex))
        {
            // Another request (usually the device retrying) stored some of these punches concurrently.
            // Fall back to one punch per transaction so every non-duplicate still lands.
            logger.LogInformation("Concurrent punch upload for device {DeviceId}; processing individually", device.Id);
            db.ClearChangeTracker();

            int recorded = 0, duplicates = 0, unmatched = 0;
            foreach (var punch in batch)
            {
                try
                {
                    var single = await ProcessAsync(device.Id, [punch], 1, ct);
                    recorded += single.Recorded;
                    duplicates += single.Duplicates;
                    unmatched += single.Unmatched;
                }
                catch (Exception inner) when (db.IsUniqueViolation(inner))
                {
                    db.ClearChangeTracker();
                    duplicates++;
                }
            }

            return new IngestResult(punches.Count, recorded, duplicates, unmatched);
        }
    }

    private async Task<IngestResult> ProcessAsync(int deviceId, IReadOnlyList<(string UserId, DateTime At)> batch, int received, CancellationToken ct)
    {
        var window = options.Value.Window;
        var from = batch[0].At;
        var to = batch[^1].At;

        var known = (await db.BiometricPunches.AsNoTracking()
                .Where(p => p.DeviceId == deviceId && p.PunchedAt >= from && p.PunchedAt <= to)
                .Select(p => new { p.DeviceUserId, p.PunchedAt })
                .ToListAsync(ct))
            .Select(p => (p.DeviceUserId, p.PunchedAt))
            .ToHashSet();

        var fresh = batch.Where(p => !known.Contains((p.UserId, p.At))).ToList();
        if (fresh.Count == 0)
        {
            return new IngestResult(received, 0, batch.Count, 0);
        }

        var biometricIds = fresh.Select(p => p.UserId).Distinct().ToList();
        var students = await db.Users.AsNoTracking()
            .Where(u => u.BiometricId != null && biometricIds.Contains(u.BiometricId))
            .Select(u => new { u.Id, u.BiometricId })
            .ToDictionaryAsync(u => u.BiometricId!, u => u.Id, ct);

        var studentIds = students.Values.ToList();
        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => studentIds.Contains(e.StudentId) && e.Mode == StudyMode.InPerson && e.Status == EnrollmentStatus.Active)
            .Select(e => new { e.Id, e.StudentId, e.CourseId })
            .ToListAsync(ct);

        var courseIds = enrollments.Select(e => e.CourseId).Distinct().ToList();
        var earliestStart = from.AddMinutes(-window.OpensMinutesBeforeStart);
        var latestStart = to.AddMinutes(window.OpensMinutesBeforeStart);
        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => courseIds.Contains(s.CourseId) && s.EndsAt >= from && s.StartsAt <= latestStart && s.StartsAt >= earliestStart.AddDays(-1))
            .Select(s => new { s.Id, s.CourseId, s.StartsAt, s.EndsAt })
            .ToListAsync(ct);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var records = await db.AttendanceRecords
            .Where(r => sessionIds.Contains(r.SessionId) && enrollmentIds.Contains(r.EnrollmentId))
            .ToDictionaryAsync(r => (r.SessionId, r.EnrollmentId), ct);

        var now = time.UtcNow;
        int recorded = 0, unmatched = 0;

        foreach (var (userId, at) in fresh)
        {
            var punch = new BiometricPunch { DeviceId = deviceId, DeviceUserId = userId, PunchedAt = at, ReceivedAt = now };
            db.BiometricPunches.Add(punch);

            if (!students.TryGetValue(userId, out var studentId))
            {
                punch.Outcome = PunchOutcome.UnknownUser;
                unmatched++;
                continue;
            }

            var matches = (
                from e in enrollments.Where(e => e.StudentId == studentId)
                from s in sessions.Where(s => s.CourseId == e.CourseId)
                where AttendanceRules.IsWithinCheckInWindow(s.StartsAt, s.EndsAt, at, window)
                select (Enrollment: e.Id, Session: s)).ToList();

            if (matches.Count == 0)
            {
                punch.Outcome = PunchOutcome.NoSession;
                unmatched++;
                continue;
            }

            punch.Outcome = PunchOutcome.AlreadyRecorded;
            foreach (var (enrollmentId, session) in matches)
            {
                var status = AttendanceRules.StatusFor(session.StartsAt, at, window);
                records.TryGetValue((session.Id, enrollmentId), out var record);

                if (record is null)
                {
                    record = new AttendanceRecord { SessionId = session.Id, EnrollmentId = enrollmentId };
                    db.AttendanceRecords.Add(record);
                    records[(session.Id, enrollmentId)] = record;
                }
                else if (record.Source == AttendanceSource.Manual || (record.Status.CountsAsAttended() && record.CheckInAt <= at))
                {
                    continue; // Instructor decisions win; the earliest fingerprint wins.
                }

                record.Status = status;
                record.Source = AttendanceSource.Biometric;
                record.CheckInAt = at;
                record.RecordedAt = now;
                punch.AttendanceRecord = record;
                punch.Outcome = PunchOutcome.Recorded;
            }

            if (punch.Outcome == PunchOutcome.Recorded)
            {
                recorded++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new IngestResult(received, recorded, batch.Count - fresh.Count, unmatched);
    }

    internal static string HashKey(string apiKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    private static DateTime TruncateToSecond(DateTime value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}

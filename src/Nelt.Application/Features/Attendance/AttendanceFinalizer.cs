using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Attendance;

public interface IAttendanceFinalizer
{
    /// <summary>Marks in-person students without a check-in as absent for every session that has ended. Idempotent.</summary>
    Task<int> FinalizeEndedSessionsAsync(CancellationToken ct = default);
}

internal sealed class AttendanceFinalizer(IAppDbContext db, IPlatformTime time, ILogger<AttendanceFinalizer> logger) : IAttendanceFinalizer
{
    private const int BatchSize = 50;

    public async Task<int> FinalizeEndedSessionsAsync(CancellationToken ct = default)
    {
        var now = time.UtcNow;
        var sessions = await db.ClassSessions
            .Where(s => !s.IsFinalized && s.EndsAt < now)
            .OrderBy(s => s.EndsAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var absences = 0;
        foreach (var session in sessions)
        {
            var missing = await db.Enrollments.AsNoTracking()
                .Where(e => e.CourseId == session.CourseId
                            && e.Mode == StudyMode.InPerson
                            && e.Status == EnrollmentStatus.Active
                            && e.ActivatedAt != null && e.ActivatedAt <= session.StartsAt
                            && !e.Attendance.Any(r => r.SessionId == session.Id))
                .Select(e => e.Id)
                .ToListAsync(ct);

            foreach (var enrollmentId in missing)
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    SessionId = session.Id,
                    EnrollmentId = enrollmentId,
                    Status = AttendanceStatus.Absent,
                    Source = AttendanceSource.System,
                    RecordedAt = now,
                });
            }

            session.IsFinalized = true;

            try
            {
                await db.SaveChangesAsync(ct);
                absences += missing.Count;
            }
            catch (Exception ex) when (db.IsUniqueViolation(ex))
            {
                // Another instance finalized this session at the same time; its result stands. The remaining
                // sessions are detached by the reset, so stop here and let the next run pick them up.
                db.ClearChangeTracker();
                logger.LogDebug("Session {SessionId} was finalized concurrently", session.Id);
                break;
            }
        }

        return absences;
    }
}

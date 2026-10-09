using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Attendance;

public interface IClassSessionService
{
    Task<Result<SessionList>> ListAsync(int courseId, CancellationToken ct = default);
    Task<Result<SessionEditModel>> GetForEditAsync(int courseId, int? sessionId, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(int courseId, SessionInput input, CancellationToken ct = default);
    Task<Result<int>> CreateSeriesAsync(int courseId, SessionSeriesInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(int courseId, int sessionId, SessionInput input, CancellationToken ct = default);
    Task<Result> DeleteAsync(int courseId, int sessionId, CancellationToken ct = default);
    Task<Result<AttendanceSheet>> SheetAsync(int courseId, int sessionId, CancellationToken ct = default);
    Task<Result> SaveSheetAsync(int courseId, int sessionId, IReadOnlyList<AttendanceMark> marks, CancellationToken ct = default);
    Task<Result<AttendanceMatrix>> MatrixAsync(int courseId, CancellationToken ct = default);
}

internal sealed class ClassSessionService(IAppDbContext db, ICourseAccess access, ICurrentUser user, IPlatformTime time) : IClassSessionService
{
    private const int MaxSeriesSessions = 300;

    public async Task<Result<SessionList>> ListAsync(int courseId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var now = time.UtcNow;
        var sessions = await db.ClassSessions.AsNoTracking().Where(s => s.CourseId == courseId)
            .OrderBy(s => s.StartsAt)
            .Select(s => new SessionRow(s.Id, s.StartsAt, s.EndsAt, s.Topic, s.Room,
                s.Records.Count(r => r.Status == AttendanceStatus.Present),
                s.Records.Count(r => r.Status == AttendanceStatus.Late),
                s.Records.Count(r => r.Status == AttendanceStatus.Absent),
                s.Records.Count(r => r.Status == AttendanceStatus.Excused),
                s.IsFinalized))
            .ToListAsync(ct);

        var students = await InPersonEnrollments(courseId).CountAsync(ct);
        return new SessionList(course.Value, students,
            sessions.Where(s => s.EndsAt >= now).ToList(),
            sessions.Where(s => s.EndsAt < now).OrderByDescending(s => s.StartsAt).ToList());
    }

    public async Task<Result<SessionEditModel>> GetForEditAsync(int courseId, int? sessionId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        if (sessionId is null)
        {
            return new SessionEditModel(course.Value, null, new SessionInput { Date = time.LocalToday, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(20, 0) });
        }

        var s = await db.ClassSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId && x.CourseId == courseId, ct);
        if (s is null)
        {
            return Error.NotFound();
        }

        var start = time.ToLocal(s.StartsAt);
        var end = time.ToLocal(s.EndsAt);
        return new SessionEditModel(course.Value, s.Id, new SessionInput
        {
            Date = DateOnly.FromDateTime(start),
            StartTime = TimeOnly.FromDateTime(start),
            EndTime = TimeOnly.FromDateTime(end),
            Topic = s.Topic,
            Room = s.Room,
        });
    }

    public async Task<Result<int>> CreateAsync(int courseId, SessionInput input, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var session = new ClassSession { CourseId = courseId };
        Apply(session, input);
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }

    public async Task<Result<int>> CreateSeriesAsync(int courseId, SessionSeriesInput input, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var existing = (await db.ClassSessions.Where(s => s.CourseId == courseId).Select(s => s.StartsAt).ToListAsync(ct)).ToHashSet();
        var created = 0;
        for (var date = input.FirstDate!.Value; date <= input.LastDate!.Value && created < MaxSeriesSessions; date = date.AddDays(1))
        {
            if (!input.Days.Contains(date.DayOfWeek))
            {
                continue;
            }

            var startsAt = time.ToUtc(date.ToDateTime(input.StartTime!.Value));
            if (existing.Contains(startsAt))
            {
                continue;
            }

            db.ClassSessions.Add(new ClassSession
            {
                CourseId = courseId,
                StartsAt = startsAt,
                EndsAt = time.ToUtc(date.ToDateTime(input.EndTime!.Value)),
                Room = string.IsNullOrWhiteSpace(input.Room) ? null : input.Room.Trim(),
            });
            created++;
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Result> UpdateAsync(int courseId, int sessionId, SessionInput input, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var session = await db.ClassSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.CourseId == courseId, ct);
        if (session is null)
        {
            return Error.NotFound();
        }

        Apply(session, input);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int courseId, int sessionId, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var session = await db.ClassSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.CourseId == courseId, ct);
        if (session is null)
        {
            return Error.NotFound();
        }

        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.BiometricPunches.Where(p => p.AttendanceRecord!.SessionId == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AttendanceRecordId, (int?)null), token);
            await db.AttendanceRecords.Where(r => r.SessionId == sessionId).ExecuteDeleteAsync(token);
            await db.ClassSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(token);
        }, ct);

        return Result.Success();
    }

    public async Task<Result<AttendanceSheet>> SheetAsync(int courseId, int sessionId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var session = await db.ClassSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.CourseId == courseId, ct);
        if (session is null)
        {
            return Error.NotFound();
        }

        var rows = await InPersonEnrollments(courseId)
            .OrderBy(e => e.Student!.FullName)
            .Select(e => new
            {
                e.Id, e.Student!.FullName, e.Student.BiometricId,
                Record = e.Attendance.Where(r => r.SessionId == sessionId).Select(r => new { r.Status, r.Source, r.CheckInAt, r.Note }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return new AttendanceSheet(course.Value, session.Id, session.StartsAt, session.EndsAt, session.Topic, session.Room,
            session.StartsAt <= time.UtcNow,
            rows.Select(r => new SheetRow(r.Id, r.FullName, r.BiometricId, r.Record?.Status, r.Record?.Source, r.Record?.CheckInAt, r.Record?.Note)).ToList());
    }

    public async Task<Result> SaveSheetAsync(int courseId, int sessionId, IReadOnlyList<AttendanceMark> marks, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        if (!await db.ClassSessions.AnyAsync(s => s.Id == sessionId && s.CourseId == courseId, ct))
        {
            return Error.NotFound();
        }

        var allowed = (await InPersonEnrollments(courseId).Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        var records = await db.AttendanceRecords.Where(r => r.SessionId == sessionId).ToDictionaryAsync(r => r.EnrollmentId, ct);
        var now = time.UtcNow;

        foreach (var mark in marks.Where(m => allowed.Contains(m.EnrollmentId)))
        {
            records.TryGetValue(mark.EnrollmentId, out var record);
            var note = string.IsNullOrWhiteSpace(mark.Note) ? null : mark.Note.Trim();

            if (mark.Status is null)
            {
                if (record is not null && record.Source != AttendanceSource.Biometric)
                {
                    await db.BiometricPunches.Where(p => p.AttendanceRecordId == record.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.AttendanceRecordId, (int?)null), ct);
                    db.AttendanceRecords.Remove(record);
                }

                continue;
            }

            if (record is null)
            {
                record = new AttendanceRecord { SessionId = sessionId, EnrollmentId = mark.EnrollmentId };
                db.AttendanceRecords.Add(record);
            }
            else if (record.Status == mark.Status && record.Note == note)
            {
                continue;
            }

            if (record.Status != mark.Status)
            {
                record.Source = AttendanceSource.Manual;
                record.RecordedById = user.UserId;
            }

            record.Status = mark.Status.Value;
            record.Note = note;
            record.RecordedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<AttendanceMatrix>> MatrixAsync(int courseId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var now = time.UtcNow;
        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => s.CourseId == courseId && s.StartsAt <= now)
            .OrderBy(s => s.StartsAt)
            .Select(s => new MatrixSession(s.Id, s.StartsAt))
            .ToListAsync(ct);

        var students = await InPersonEnrollments(courseId).OrderBy(e => e.Student!.FullName)
            .Select(e => new { e.Id, e.Student!.FullName }).ToListAsync(ct);

        var records = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.Session!.CourseId == courseId)
            .Select(r => new { r.EnrollmentId, r.SessionId, r.Status })
            .ToListAsync(ct);
        var lookup = records.ToDictionary(r => (r.EnrollmentId, r.SessionId), r => r.Status);

        var rows = students.Select(st =>
        {
            var cells = sessions.Select(s => new MatrixCell(lookup.TryGetValue((st.Id, s.Id), out var status) ? status : null)).ToList();
            var attended = cells.Count(c => c.Status is { } s && s.CountsAsAttended());
            var absent = cells.Count(c => c.Status == AttendanceStatus.Absent);
            return new MatrixRow(st.Id, st.FullName, AttendanceRules.Rate(attended, attended + absent), cells);
        }).ToList();

        var minimum = await db.Courses.Where(c => c.Id == courseId).Select(c => c.Policy.MinAttendanceRate).FirstAsync(ct);
        return new AttendanceMatrix(course.Value, minimum, sessions, rows);
    }

    private IQueryable<Enrollment> InPersonEnrollments(int courseId)
        => db.Enrollments.Where(e => e.CourseId == courseId && e.Mode == StudyMode.InPerson
                                     && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed));

    private void Apply(ClassSession session, SessionInput input)
    {
        var date = input.Date!.Value;
        session.StartsAt = time.ToUtc(date.ToDateTime(input.StartTime!.Value));
        session.EndsAt = time.ToUtc(date.ToDateTime(input.EndTime!.Value));
        session.Topic = string.IsNullOrWhiteSpace(input.Topic) ? null : input.Topic.Trim();
        session.Room = string.IsNullOrWhiteSpace(input.Room) ? null : input.Room.Trim();
        if (session.EndsAt > time.UtcNow)
        {
            session.IsFinalized = false;
        }
    }
}

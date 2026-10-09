using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Common;
using Nelt.Application.Features.Attendance;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>Class sessions, attendance sheets, the absence job, fingerprint ingestion and devices.</summary>
[Collection(NeltCollection.Name)]
public sealed class AttendanceServiceTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    /// <summary>A fresh course for the instructor with one active in-person student activated before every session.</summary>
    private async Task<(int CourseId, int EnrollmentId)> CourseWithStudentAsync(string title)
    {
        var studentId = await Fixture.CreateStudentAsync();
        await using var scope = Fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course
        {
            Slug = $"attendance-{Guid.NewGuid():N}"[..40],
            LevelId = Seed.LevelA1Id,
            InstructorId = Seed.InstructorId,
            Title = LocalizedText.Of(title),
            DeliveryMode = DeliveryMode.InPerson,
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var enrollment = new Enrollment { CourseId = course.Id, StudentId = studentId, Mode = StudyMode.InPerson, Status = EnrollmentStatus.Active, ActivatedAt = DateTime.UtcNow.AddDays(-30) };
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();
        return (course.Id, enrollment.Id);
    }

    [Fact]
    public async Task Instructors_plan_single_sessions_and_weekly_series()
    {
        var (courseId, _) = await CourseWithStudentAsync("Planning course");
        using var _ = TestUser.As(Seed.InstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var sessions = scope.ServiceProvider.GetRequiredService<IClassSessionService>();
        var time = scope.ServiceProvider.GetRequiredService<IPlatformTime>();
        var monday = NextMonday(time.LocalToday);

        var single = Ok(await sessions.CreateAsync(courseId, new SessionInput { Date = monday.AddDays(-7 * 4), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(11, 0), Topic = "Past class" }));
        var series = new SessionSeriesInput
        {
            FirstDate = monday,
            LastDate = monday.AddDays(13),
            Days = [DayOfWeek.Monday, DayOfWeek.Wednesday],
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(20, 0),
            Room = "Lab",
        };

        Assert.Equal(4, Ok(await sessions.CreateSeriesAsync(courseId, series)));
        Assert.Equal(0, Ok(await sessions.CreateSeriesAsync(courseId, series))); // Existing start times are skipped.

        var list = Ok(await sessions.ListAsync(courseId));
        Assert.Equal(4, list.Upcoming.Count);
        Assert.Contains(list.Past, s => s.Id == single);
        Assert.Equal(1, list.InPersonStudents);

        var edit = Ok(await sessions.GetForEditAsync(courseId, single));
        Assert.Equal(new TimeOnly(9, 0), edit.Input.StartTime);
        Ok(await sessions.UpdateAsync(courseId, single, new SessionInput { Date = edit.Input.Date, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0) }));
        Ok(await sessions.DeleteAsync(courseId, list.Upcoming[0].Id));
        Assert.Equal(3, Ok(await sessions.ListAsync(courseId)).Upcoming.Count);
    }

    [Fact]
    public async Task Attendance_sheet_marks_are_saved_and_counted_in_the_matrix()
    {
        var (courseId, enrollmentId) = await CourseWithStudentAsync("Sheet course");
        using var _ = TestUser.As(Seed.InstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var sessions = scope.ServiceProvider.GetRequiredService<IClassSessionService>();
        var time = scope.ServiceProvider.GetRequiredService<IPlatformTime>();
        var yesterday = time.LocalToday.AddDays(-1);

        var first = Ok(await sessions.CreateAsync(courseId, new SessionInput { Date = yesterday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0) }));
        var second = Ok(await sessions.CreateAsync(courseId, new SessionInput { Date = yesterday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0) }));

        Ok(await sessions.SaveSheetAsync(courseId, first, [new AttendanceMark { EnrollmentId = enrollmentId, Status = AttendanceStatus.Present }]));
        Ok(await sessions.SaveSheetAsync(courseId, second, [new AttendanceMark { EnrollmentId = enrollmentId, Status = AttendanceStatus.Absent, Note = "Sick" }]));
        // Marks for students outside the course are ignored.
        Ok(await sessions.SaveSheetAsync(courseId, second, [new AttendanceMark { EnrollmentId = Seed.StudentEnrollmentId, Status = AttendanceStatus.Present }]));

        var sheet = Ok(await sessions.SheetAsync(courseId, second));
        var row = Assert.Single(sheet.Rows);
        Assert.Equal(AttendanceStatus.Absent, row.Status);
        Assert.Equal(AttendanceSource.Manual, row.Source);
        Assert.Equal("Sick", row.Note);

        var matrix = Ok(await sessions.MatrixAsync(courseId));
        var matrixRow = Assert.Single(matrix.Rows);
        Assert.Equal(50m, matrixRow.Rate);

        // Clearing a manual mark removes it.
        Ok(await sessions.SaveSheetAsync(courseId, second, [new AttendanceMark { EnrollmentId = enrollmentId, Status = null }]));
        Assert.Null(Assert.Single(Ok(await sessions.SheetAsync(courseId, second)).Rows).Status);
    }

    [Fact]
    public async Task Finalizer_marks_missing_students_absent_once()
    {
        var (courseId, enrollmentId) = await CourseWithStudentAsync("Finalizer course");
        int sessionId;
        await using (var setup = Fixture.Scope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = new ClassSession { CourseId = courseId, StartsAt = DateTime.UtcNow.AddHours(-5), EndsAt = DateTime.UtcNow.AddHours(-3) };
            db.ClassSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        // The job works in batches of 50 ended sessions; run it until this session is done.
        for (var i = 0; i < 10; i++)
        {
            await using var scope = Fixture.Scope();
            await scope.ServiceProvider.GetRequiredService<IAttendanceFinalizer>().FinalizeEndedSessionsAsync();
            var finalized = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ClassSessions.AnyAsync(s => s.Id == sessionId && s.IsFinalized);
            if (finalized)
            {
                break;
            }
        }

        await using (var check = Fixture.Scope())
        {
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.AttendanceRecords.SingleAsync(r => r.SessionId == sessionId && r.EnrollmentId == enrollmentId);
            Assert.Equal(AttendanceStatus.Absent, record.Status);
            Assert.Equal(AttendanceSource.System, record.Source);

            await check.ServiceProvider.GetRequiredService<IAttendanceFinalizer>().FinalizeEndedSessionsAsync();
            Assert.Equal(1, await db.AttendanceRecords.CountAsync(r => r.SessionId == sessionId));
        }
    }

    [Fact]
    public async Task Fingerprint_punches_mark_presence_ignore_duplicates_and_report_unknown_ids()
    {
        await using var scope = Fixture.Scope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IBiometricIngestionService>();
        var time = scope.ServiceProvider.GetRequiredService<IPlatformTime>();
        var device = await ingestion.AuthenticateByKeyAsync(SeedData.DeviceKey);
        Assert.NotNull(device);
        Assert.NotNull(await ingestion.AuthenticateBySerialAsync(SeedData.DeviceSerial));
        Assert.Null(await ingestion.AuthenticateByKeyAsync("nelt_wrong"));

        var local = time.ToLocal(DateTime.UtcNow);
        var first = await ingestion.IngestAsync(device, [new RawPunch(SeedData.StudentBiometricId, local), new RawPunch("424242", local)]);
        Assert.Equal(2, first.Received);
        Assert.Equal(1, first.Unmatched);

        var again = await ingestion.IngestAsync(device, [new RawPunch(SeedData.StudentBiometricId, local)]);
        Assert.Equal(1, again.Duplicates);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.AttendanceRecords.AsNoTracking().SingleAsync(r => r.SessionId == Seed.OpenSessionId && r.EnrollmentId == Seed.StudentEnrollmentId);
        Assert.True(record.Status.CountsAsAttended());
        Assert.Equal(AttendanceSource.Biometric, record.Source);

        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var adminScope = Fixture.Scope();
        var punches = await adminScope.ServiceProvider.GetRequiredService<IDeviceService>().PunchesAsync(Seed.DeviceId, PunchOutcome.UnknownUser, page: 1);
        Assert.Contains(punches.Items, p => p.DeviceUserId == "424242");
    }

    [Fact]
    public async Task Devices_are_registered_with_a_one_time_key_that_can_be_rotated()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceService>();
        var ingestion = scope.ServiceProvider.GetRequiredService<IBiometricIngestionService>();

        var created = Ok(await devices.CreateAsync(new DeviceInput { Name = "Back door", SerialNumber = "TESTSN-BACK" }));
        Assert.StartsWith("nelt_", created.ApiKey, StringComparison.Ordinal);
        Assert.NotNull(await ingestion.AuthenticateByKeyAsync(created.ApiKey));
        Fails(await devices.CreateAsync(new DeviceInput { Name = "Duplicate", SerialNumber = "TESTSN-BACK" }), ErrorKind.Validation);

        var rotated = Ok(await devices.RotateKeyAsync(created.DeviceId));
        Assert.Null(await ingestion.AuthenticateByKeyAsync(created.ApiKey));
        Assert.NotNull(await ingestion.AuthenticateByKeyAsync(rotated.ApiKey));

        Ok(await devices.UpdateAsync(created.DeviceId, new DeviceInput { Name = "Back door", SerialNumber = "TESTSN-BACK", IsActive = false }));
        Assert.Null(await ingestion.AuthenticateByKeyAsync(rotated.ApiKey)); // Inactive devices are refused.

        Assert.Contains(await devices.ListAsync(), d => d.Id == created.DeviceId);
        Ok(await devices.DeleteAsync(created.DeviceId));
        Fails(await devices.DeleteAsync(created.DeviceId), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Devices_that_sent_punches_cannot_be_deleted()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceService>();
        var ingestion = scope.ServiceProvider.GetRequiredService<IBiometricIngestionService>();
        var created = Ok(await devices.CreateAsync(new DeviceInput { Name = "Side door", SerialNumber = "TESTSN-SIDE" }));
        var device = await ingestion.AuthenticateByKeyAsync(created.ApiKey);
        Assert.NotNull(device);

        await ingestion.IngestAsync(device, [new RawPunch("777777", new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified))]);

        Fails(await devices.DeleteAsync(created.DeviceId), ErrorKind.Conflict);
    }

    [Fact]
    public async Task Other_instructors_cannot_touch_attendance()
    {
        using var _ = TestUser.As(Seed.OtherInstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var sessions = scope.ServiceProvider.GetRequiredService<IClassSessionService>();

        Fails(await sessions.SheetAsync(Seed.CourseId, Seed.PastSessionId), ErrorKind.Forbidden);
        Fails(await sessions.SaveSheetAsync(Seed.CourseId, Seed.PastSessionId, []), ErrorKind.Forbidden);
        Fails(await sessions.DeleteAsync(Seed.CourseId, Seed.PastSessionId), ErrorKind.Forbidden);
    }

    private static DateOnly NextMonday(DateOnly today)
    {
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }
}

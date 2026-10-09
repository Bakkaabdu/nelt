using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Common;
using Nelt.Application.Features.Dashboard;
using Nelt.Application.Features.Events;
using Nelt.Application.Features.Levels;
using Nelt.Application.Features.Materials;
using Nelt.Application.Features.Progress;
using Nelt.Application.Features.Settings;
using Nelt.Application.Features.Users;
using Nelt.Domain.Common;
using Nelt.Domain.Enums;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>Levels, users, platform settings, events, the materials library, dashboards and progress reports.</summary>
[Collection(NeltCollection.Name)]
public sealed class AdministrationTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task Levels_are_created_validated_and_deleted()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var levels = scope.ServiceProvider.GetRequiredService<ILevelService>();

        var input = new LevelInput { Language = TargetLanguage.German, Code = "t9", Rank = 90, Name = LocalizedText.Of("Test level") };
        var id = Ok(await levels.CreateAsync(input));
        Assert.Equal("T9", (await levels.GetForEditAsync(id))?.Code);

        Fails(await levels.CreateAsync(new LevelInput { Language = TargetLanguage.German, Code = "T9", Rank = 91, Name = LocalizedText.Of("Dup code") }), ErrorKind.Validation);
        Fails(await levels.CreateAsync(new LevelInput { Language = TargetLanguage.German, Code = "T8", Rank = 90, Name = LocalizedText.Of("Dup rank") }), ErrorKind.Validation);
        Ok(await levels.CreateAsync(new LevelInput { Language = TargetLanguage.Chinese, Code = "T9", Rank = 90, Name = LocalizedText.Of("Same code, other language") }));

        Fails(await levels.DeleteAsync(Seed.LevelA1Id), ErrorKind.Conflict); // Has courses and materials.
        Ok(await levels.DeleteAsync(id));
        Assert.Contains(await levels.ListAsync(), l => l.Id == Seed.LevelA1Id && l.CourseCount > 0 && l.MaterialCount > 0);
        Assert.NotEmpty(await levels.OptionsAsync());
    }

    [Fact]
    public async Task Users_are_created_edited_searched_and_deactivated()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var users = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

        var input = new UserInput { FullName = "Nadia New", Email = "nadia.new@nelt.test", Role = Roles.Instructor, Password = NeltFixture.Password, BiometricId = "5005" };
        var id = Ok(await users.CreateAsync(input));

        Fails(await users.CreateAsync(new UserInput { FullName = "No password", Email = "nopass@nelt.test", Role = Roles.Student }), ErrorKind.Validation);
        Fails(await users.CreateAsync(new UserInput { FullName = "Same email", Email = "nadia.new@nelt.test", Role = Roles.Student, Password = NeltFixture.Password }), ErrorKind.Validation);
        var fingerprint = Fails(await users.CreateAsync(new UserInput { FullName = "Same fingerprint", Email = "fp@nelt.test", Role = Roles.Student, Password = NeltFixture.Password, BiometricId = "5005" }), ErrorKind.Validation);
        Assert.Equal(nameof(UserInput.BiometricId), fingerprint.Field);

        var instructors = await users.ListAsync(new UserFilter(Roles.Instructor, "Nadia"));
        var row = Assert.Single(instructors.Items);
        Assert.Equal(Roles.Instructor, row.Role);

        Ok(await users.UpdateAsync(id, new UserInput { FullName = "Nadia Renamed", Email = "nadia.renamed@nelt.test", Role = Roles.Student, IsActive = false }));
        var edit = await users.GetForEditAsync(id);
        Assert.NotNull(edit);
        Assert.Equal("Nadia Renamed", edit.Input.FullName);
        Assert.Equal(Roles.Student, edit.Input.Role);
        Assert.False(edit.Input.IsActive);

        // Inactive students are not offered for enrollment.
        Assert.DoesNotContain(await users.SearchStudentsAsync("Nadia"), s => s.Id == id);
        Assert.Contains(await users.SearchStudentsAsync("Sara"), s => s.Id == Seed.StudentId);
        Assert.Null(await users.GetForEditAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deactivated_accounts_cannot_sign_in()
    {
        var user = await Fixture.CreateUserAsync(Roles.Student);
        using (TestUser.As(Seed.AdminId, Roles.Admin))
        {
            await using var scope = Fixture.Scope();
            Ok(await scope.ServiceProvider.GetRequiredService<IUserAdminService>().UpdateAsync(user.Id,
                new UserInput { FullName = user.FullName, Email = user.Email!, Role = Roles.Student, IsActive = false }));
        }

        using var client = Fixture.CreateClient();
        using var response = await NeltFixture.SignInAsync(client, user.Email!, NeltFixture.Password);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); // Login form shown again, no redirect.
    }

    [Fact]
    public async Task Admins_cannot_remove_their_own_admin_access()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var users = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

        var me = await users.GetForEditAsync(Seed.AdminId);
        Assert.NotNull(me);
        Assert.True(me.IsSelf);
        var input = me.Input;
        input.Role = Roles.Instructor;

        Fails(await users.UpdateAsync(Seed.AdminId, input), ErrorKind.Validation);
    }

    [Fact]
    public async Task Platform_settings_are_saved_and_served()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var settings = scope.ServiceProvider.GetRequiredService<IPlatformSettingsService>();

        var input = await settings.GetForEditAsync();
        input.ContactEmail = "hello@nelt.test";
        input.HeroTitle = LocalizedText.Of("Learn German and Chinese", "تعلّم الألمانية والصينية");
        input.Currency = "lyd";

        Ok(await settings.UpdateAsync(input));

        var site = await settings.GetAsync();
        Assert.Equal("hello@nelt.test", site.ContactEmail);
        Assert.Equal("LYD", site.Currency);
        Assert.Equal("تعلّم الألمانية والصينية", site.HeroTitle.Get("ar"));
    }

    [Fact]
    public async Task Events_are_managed_per_course_by_instructors_and_platform_wide_by_admins()
    {
        var far = DateTime.UtcNow.AddDays(200); // Far ahead so it never crowds the dashboards' "next events".
        await using var scope = Fixture.Scope();
        var events = scope.ServiceProvider.GetRequiredService<IEventService>();

        int courseEvent;
        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            var form = Ok(await events.GetForEditAsync(null));
            Assert.True(form.CourseRequired);
            Assert.Contains(form.Courses, c => c.Id == Seed.CourseId);
            Assert.DoesNotContain(form.Courses, c => c.Id == Seed.OtherCourseId);

            Fails(await events.CreateAsync(NewEvent(null, far)), ErrorKind.Validation);
            Fails(await events.CreateAsync(NewEvent(Seed.OtherCourseId, far)), ErrorKind.Validation);
            courseEvent = Ok(await events.CreateAsync(NewEvent(Seed.CourseId, far)));
            Ok(await events.UpdateAsync(courseEvent, NewEvent(Seed.CourseId, far.AddHours(1), "Renamed visit")));
            Fails(await events.UpdateAsync(Seed.PublicEventId, NewEvent(Seed.CourseId, far)), ErrorKind.NotFound); // Platform events are admin-only.
            Assert.Contains(await events.ListAsync(includePast: false), e => e.Id == courseEvent && e.Title.En == "Renamed visit");
        }

        using (TestUser.As(Seed.OtherInstructorId, Roles.Instructor))
        {
            Fails(await events.DeleteAsync(courseEvent), ErrorKind.NotFound);
        }

        using (TestUser.As(Seed.AdminId, Roles.Admin))
        {
            var platform = Ok(await events.CreateAsync(NewEvent(null, far)));
            Assert.Contains(await events.ListAsync(includePast: false), e => e.Id == platform && e.IsPublic);
            Assert.DoesNotContain(await events.ListAsync(includePast: true), e => e.Id == platform);
            Ok(await events.DeleteAsync(platform));
            Ok(await events.DeleteAsync(courseEvent));
        }
    }

    private static EventInput NewEvent(int? courseId, DateTime utcStart, string title = "Delegation visit") => new()
    {
        CourseId = courseId,
        Type = EventType.DelegationVisit,
        Title = LocalizedText.Of(title),
        Description = LocalizedText.Of("Guests from Berlin."),
        Location = "Hall",
        StartsAt = utcStart,
        EndsAt = utcStart.AddHours(2),
        IsPublic = courseId is null,
    };

    [Fact]
    public async Task Materials_library_upload_edit_move_and_delete()
    {
        using var _ = TestUser.As(Seed.InstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var materials = scope.ServiceProvider.GetRequiredService<IMaterialService>();

        var library = await materials.LibraryAsync(Seed.LevelA1Id);
        Assert.Equal(Seed.LevelA1Id, library.Selected?.Id);
        Assert.DoesNotContain(library.Levels, l => l.Id == Seed.LevelA2Id); // Only levels the instructor teaches.

        using var first = new MemoryStream(Encoding.UTF8.GetBytes("worksheet one"));
        var a = Ok(await materials.UploadAsync(new MaterialInput { LevelId = Seed.LevelA1Id, Type = MaterialType.Worksheet, Title = "Sheet 1" }, new FileUpload(first, "sheet1.txt", first.Length)));
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("worksheet two"));
        var b = Ok(await materials.UploadAsync(new MaterialInput { LevelId = Seed.LevelA1Id, CourseId = Seed.CourseId, Type = MaterialType.Worksheet, Title = "Sheet 2" }, new FileUpload(second, "sheet2.txt", second.Length)));
        using var audio = new MemoryStream(new byte[64]);
        var c = Ok(await materials.UploadAsync(new MaterialInput { LevelId = Seed.LevelA1Id, Type = MaterialType.Document, Title = "Listening" }, new FileUpload(audio, "track.mp3", audio.Length)));

        Ok(await materials.MoveAsync(b, -1));
        library = await materials.LibraryAsync(Seed.LevelA1Id);
        var worksheets = library.Groups[MaterialType.Worksheet].Select(m => m.Id).ToList();
        Assert.True(worksheets.IndexOf(b) < worksheets.IndexOf(a));
        Assert.Contains(library.Groups[MaterialType.Audio], m => m.Id == c); // Audio files are filed as audio automatically.

        var edit = Ok(await materials.GetForEditAsync(a));
        edit.Title = "Sheet 1 (v2)";
        edit.IsPublished = false;
        Ok(await materials.UpdateAsync(a, edit));

        using var bad = new MemoryStream(Encoding.UTF8.GetBytes("MZ"));
        Fails(await materials.UploadAsync(new MaterialInput { LevelId = Seed.LevelA1Id, Title = "Virus" }, new FileUpload(bad, "setup.exe", bad.Length)), ErrorKind.Validation);
        Fails(await materials.UploadAsync(new MaterialInput { LevelId = Seed.LevelA2Id, Title = "Wrong level" }, new FileUpload(bad, "notes.txt", bad.Length)), ErrorKind.Validation);
        Fails(await materials.UploadAsync(new MaterialInput { LevelId = Seed.LevelA1Id, CourseId = Seed.OtherCourseId, Title = "Wrong course" }, new FileUpload(bad, "notes.txt", bad.Length)), ErrorKind.Validation);

        Ok(await materials.DeleteAsync(a));
        Ok(await materials.DeleteAsync(b));
        Ok(await materials.DeleteAsync(c));
        Fails(await materials.DeleteAsync(a), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Instructors_cannot_edit_materials_of_levels_they_do_not_teach()
    {
        using var _ = TestUser.As(Seed.OtherInstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var materials = scope.ServiceProvider.GetRequiredService<IMaterialService>();

        Fails(await materials.GetForEditAsync(Seed.MaterialId), ErrorKind.NotFound);
        Fails(await materials.DeleteAsync(Seed.MaterialId), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Dashboards_and_progress_reports_load()
    {
        await using var scope = Fixture.Scope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var progress = scope.ServiceProvider.GetRequiredService<IProgressService>();

        using (TestUser.As(Seed.AdminId, Roles.Admin))
        {
            var admin = await dashboard.AdminAsync();
            Assert.True(admin.Students >= 5);
            Assert.True(admin.Instructors >= 2);
            Assert.True(admin.PendingEnrollments >= 1);
            Assert.True(admin.RevenueThisMonth >= 0);
            Assert.NotEmpty(admin.RecentEnrollments);
        }

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            var teaching = await dashboard.TeachingAsync();
            Assert.Contains(teaching, c => c.Id == Seed.CourseId && c.SubmissionsToGrade >= 1 && c.NextSession is not null);
            Assert.DoesNotContain(teaching, c => c.Id == Seed.OtherCourseId);

            var overview = Ok(await dashboard.CourseOverviewAsync(Seed.CourseId));
            Assert.True(overview.ActiveStudents >= 2);
            Assert.NotEmpty(overview.NextSessions);

            var roster = Ok(await progress.RosterAsync(Seed.CourseId));
            Assert.Contains(roster.Rows, r => r.EnrollmentId == Seed.StudentEnrollmentId);
            Assert.DoesNotContain(roster.Rows, r => r.EnrollmentId == Seed.PendingEnrollmentId);

            var student = Ok(await progress.StudentAsync(Seed.CourseId, Seed.OnlineEnrollmentId));
            Assert.Equal("Omar Online", student.StudentName);
            Assert.Equal(2, student.Lessons.Count);
            Assert.Contains(student.Submissions, s => s.SubmittedAt is not null);
            Fails(await progress.StudentAsync(Seed.CourseId, Seed.PendingEnrollmentId + 100000), ErrorKind.NotFound);
        }

        using (TestUser.As(Seed.OtherInstructorId, Roles.Instructor))
        {
            Fails(await progress.RosterAsync(Seed.CourseId), ErrorKind.Forbidden);
            Fails(await dashboard.CourseOverviewAsync(Seed.CourseId), ErrorKind.Forbidden);
        }
    }

    [Fact]
    public async Task Submissions_are_graded_from_the_seeded_course()
    {
        using var _ = TestUser.As(Seed.InstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var assignments = scope.ServiceProvider.GetRequiredService<Nelt.Application.Features.Assignments.IAssignmentService>();

        var page = Ok(await assignments.SubmissionsAsync(Seed.CourseId, Seed.AssignmentId));
        Assert.Contains(page.Rows, r => r.SubmissionId == Seed.GradableSubmissionId);
        Fails(await assignments.GradeAsync(Seed.OtherCourseId, Seed.GradableSubmissionId, new Nelt.Application.Features.Assignments.GradeInput { Score = 5 }), ErrorKind.Forbidden);
    }
}

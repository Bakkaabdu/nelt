using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Common;
using Nelt.Application.Features.Catalog;
using Nelt.Application.Features.Files;
using Nelt.Application.Features.Learning;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>The student area, the public catalogue and protected file access.</summary>
[Collection(NeltCollection.Name)]
public sealed class LearningServiceTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task Student_dashboard_lists_courses_sessions_assignments_and_events()
    {
        using var _ = TestUser.As(Seed.StudentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        var dashboard = await learning.DashboardAsync();

        Assert.Equal("Sara Student", dashboard.StudentName);
        var course = Assert.Single(dashboard.Courses, c => c.CourseId == Seed.CourseId);
        Assert.Equal(2, course.LessonsTotal); // The unpublished lesson is not counted.
        Assert.Contains(dashboard.Sessions, s => s.CourseId == Seed.CourseId);
        Assert.Contains(dashboard.DueAssignments, a => a.AssignmentId == Seed.AssignmentId);
        Assert.Contains(dashboard.Events, e => e.Id == Seed.CourseEventId);
        Assert.Contains(dashboard.Events, e => e.Id == Seed.PublicEventId);
    }

    /// <summary>Regression for "new EventCard(...).StartsAt &lt;= @horizon could not be translated" on /learn/schedule.</summary>
    [Fact]
    public async Task Schedule_combines_classes_and_events()
    {
        using var _ = TestUser.As(Seed.StudentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        var schedule = await learning.ScheduleAsync();

        Assert.Contains(schedule, i => i.IsClass && i.Title.En == "Numbers");
        Assert.Contains(schedule, i => !i.IsClass && i.Title.En == "Conversation club");
        Assert.Contains(schedule, i => !i.IsClass && i.Title.En == "Oktoberfest evening");
        Assert.Equal(schedule.OrderBy(i => i.StartsAt).Select(i => i.StartsAt), schedule.Select(i => i.StartsAt));
    }

    [Fact]
    public async Task Online_students_see_events_but_no_classes()
    {
        using var _ = TestUser.As(Seed.OnlineStudentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        var schedule = await learning.ScheduleAsync();

        Assert.DoesNotContain(schedule, i => i.IsClass);
        Assert.Contains(schedule, i => i.Title.En == "Conversation club");
    }

    [Fact]
    public async Task Course_home_shows_lessons_quizzes_and_evaluation()
    {
        using var _ = TestUser.As(Seed.StudentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        var home = Ok(await learning.CourseHomeAsync(Seed.CourseId));

        Assert.Equal(StudyMode.InPerson, home.Mode);
        Assert.Equal(2, home.Lessons.Count);
        Assert.Contains(home.Quizzes, q => q.Id == Seed.QuizId && q.QuestionCount == 2);
        Assert.Contains(home.Quizzes, q => q.Id == Seed.FinalExamId);
        Assert.NotEmpty(home.Sessions);
        Assert.NotNull(home.Evaluation);
    }

    [Fact]
    public async Task Course_home_is_blocked_for_pending_and_unrelated_students()
    {
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        using (TestUser.As(Seed.PendingStudentId, Roles.Student))
        {
            Fails(await learning.CourseHomeAsync(Seed.CourseId), ErrorKind.Forbidden);
        }

        using (TestUser.As(Seed.OutsiderId, Roles.Student))
        {
            Fails(await learning.CourseHomeAsync(Seed.CourseId), ErrorKind.NotFound);
        }
    }

    [Fact]
    public async Task Opening_and_completing_lessons_tracks_progress()
    {
        var studentId = await Fixture.CreateStudentAsync();
        await using (var setup = Fixture.Scope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Enrollments.Add(new Domain.Entities.Enrollment
            {
                CourseId = Seed.CourseId,
                StudentId = studentId,
                Mode = StudyMode.Online,
                Status = EnrollmentStatus.Active,
                ActivatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var _ = TestUser.As(studentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        var player = Ok(await learning.LessonAsync(Seed.CourseId, Seed.LessonId));
        Assert.Equal(Seed.PreviewLessonId, player.PreviousLessonId);
        Assert.NotNull(player.EmbedUrl);
        Fails(await learning.LessonAsync(Seed.CourseId, Seed.UnpublishedLessonId), ErrorKind.NotFound);

        Ok(await learning.CompleteLessonAsync(Seed.CourseId, Seed.LessonId));
        Ok(await learning.CompleteLessonAsync(Seed.CourseId, Seed.LessonId)); // Idempotent (video "ended" + button click).

        var home = Ok(await learning.CourseHomeAsync(Seed.CourseId));
        Assert.Equal(1, home.LessonsCompleted);
        Assert.Equal(Seed.PreviewLessonId, home.ContinueLessonId);
    }

    [Fact]
    public async Task Free_preview_lessons_are_public_and_others_are_not()
    {
        using var _ = TestUser.Anonymous();
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        var preview = Ok(await learning.PreviewAsync(Seed.CourseSlug, Seed.PreviewLessonId));
        Assert.True(preview.IsPreviewMode);
        Assert.All(preview.Outline, l => Assert.Equal(Seed.PreviewLessonId, l.Id));

        Fails(await learning.PreviewAsync(Seed.CourseSlug, Seed.LessonId), ErrorKind.NotFound);
        Fails(await learning.PreviewAsync(Seed.DraftCourseSlug, Seed.PreviewLessonId), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Materials_and_attendance_pages_load_for_students()
    {
        using var _ = TestUser.As(Seed.StudentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var learning = scope.ServiceProvider.GetRequiredService<ILearningService>();

        var shelf = Ok(await learning.MaterialsAsync(Seed.CourseId));
        Assert.Contains(shelf.Groups.SelectMany(g => g.Items), m => m.Id == Seed.MaterialId);

        var attendance = Ok(await learning.AttendanceAsync(Seed.CourseId));
        Assert.Equal(StudyMode.InPerson, attendance.Mode);
        Assert.True(attendance.Entries.Count >= 3);
    }

    [Fact]
    public async Task Catalogue_search_filters_and_details_work()
    {
        using var _ = TestUser.Anonymous();
        await using var scope = Fixture.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var landing = await catalog.GetLandingAsync();
        Assert.NotEmpty(landing.Tracks);
        Assert.Contains(landing.FeaturedCourses, c => c.Id == Seed.CourseId);

        var all = await catalog.SearchAsync(new CourseFilter());
        Assert.Contains(all.Courses, c => c.Id == Seed.CourseId);
        Assert.DoesNotContain(all.Courses, c => c.Id == Seed.DraftCourseId);

        var a2 = await catalog.SearchAsync(new CourseFilter(TargetLanguage.German, Seed.LevelA2Id, DeliveryMode.InPerson));
        Assert.Contains(a2.Courses, c => c.Id == Seed.OtherCourseId);
        Assert.DoesNotContain(a2.Courses, c => c.Id == Seed.CourseId);

        var chinese = await catalog.SearchAsync(new CourseFilter(TargetLanguage.Chinese));
        Assert.DoesNotContain(chinese.Courses, c => c.Id == Seed.CourseId);

        var details = await catalog.GetCourseAsync(Seed.CourseSlug);
        Assert.NotNull(details);
        Assert.Equal("Ines Instructor", details.InstructorName);
        Assert.Equal(2, details.Lessons.Count);
        Assert.True(details.HasFinalExam);
        Assert.Equal(Seed.OtherCourseId, details.NextLevelCourse?.Id);
        Assert.True(details.Card.SeatsLeft is > 0 and < 30);

        Assert.Null(await catalog.GetCourseAsync(Seed.DraftCourseSlug));
        Assert.Contains(await catalog.PublicEventsAsync(), e => e.Id == Seed.PublicEventId);
        Assert.DoesNotContain(await catalog.PublicEventsAsync(), e => e.Id == Seed.CourseEventId);
    }

    [Fact]
    public async Task Signed_in_visitors_see_their_enrollment_on_the_course_page()
    {
        using var _ = TestUser.As(Seed.PendingStudentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var details = await catalog.GetCourseAsync(Seed.CourseSlug);

        Assert.Equal(EnrollmentStatus.Pending, details?.Viewer?.Status);
    }

    [Fact]
    public async Task Protected_files_are_only_resolved_for_allowed_users()
    {
        await using var scope = Fixture.Scope();
        var files = scope.ServiceProvider.GetRequiredService<IFileAccessService>();

        using (TestUser.As(Seed.StudentId, Roles.Student))
        {
            Assert.NotNull(await files.ResolveAsync(ProtectedFileKind.Material, Seed.MaterialId));
        }

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            Assert.NotNull(await files.ResolveAsync(ProtectedFileKind.Material, Seed.MaterialId));
        }

        using (TestUser.As(Seed.OutsiderId, Roles.Student))
        {
            Assert.Null(await files.ResolveAsync(ProtectedFileKind.Material, Seed.MaterialId));
        }

        using (TestUser.As(Seed.PendingStudentId, Roles.Student))
        {
            Assert.Null(await files.ResolveAsync(ProtectedFileKind.Material, Seed.MaterialId));
        }

        using (TestUser.Anonymous())
        {
            Assert.Null(await files.ResolveAsync(ProtectedFileKind.Material, Seed.MaterialId));
            Assert.Null(await files.CoverKeyAsync(Seed.DraftCourseId));
        }
    }
}

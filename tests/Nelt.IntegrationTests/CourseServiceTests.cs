using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Common;
using Nelt.Application.Features.Attendance;
using Nelt.Application.Features.Courses;
using Nelt.Application.Features.Lessons;
using Nelt.Application.Features.Quizzes;
using Nelt.Domain.Common;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>Course administration and lesson management, run against SQL Server.</summary>
[Collection(NeltCollection.Name)]
public sealed class CourseServiceTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    private CourseInput NewCourse(string title, Guid? instructorId = null) => new()
    {
        LevelId = Seed.LevelA1Id,
        InstructorId = instructorId,
        Title = LocalizedText.Of(title),
        Summary = LocalizedText.Of("Summary"),
        Price = 99,
        DeliveryMode = DeliveryMode.Hybrid,
    };

    [Fact]
    public async Task Instructor_list_contains_staff_only()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();

        var instructors = await courses.InstructorsAsync();

        Assert.Contains(instructors, u => u.Id == Seed.InstructorId);
        Assert.Contains(instructors, u => u.Id == Seed.AdminId);
        Assert.DoesNotContain(instructors, u => u.Id == Seed.StudentId);
    }

    /// <summary>Regression for "The LINQ expression ... new UserOption(...).Id == @instructorId could not be translated".</summary>
    [Fact]
    public async Task Creating_a_course_with_an_instructor_works()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();

        var id = Ok(await courses.CreateAsync(NewCourse("Service course with instructor", Seed.InstructorId), cover: null));

        var edit = await courses.GetForEditAsync(id);
        Assert.NotNull(edit);
        Assert.Equal(Seed.InstructorId, edit.Input.InstructorId);
    }

    [Fact]
    public async Task A_student_cannot_be_assigned_as_instructor()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();

        var error = Fails(await courses.CreateAsync(NewCourse("Course with a student as teacher", Seed.StudentId), cover: null), ErrorKind.Validation);

        Assert.Equal(nameof(CourseInput.InstructorId), error.Field);
    }

    [Fact]
    public async Task An_unknown_level_is_rejected()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();
        var input = NewCourse("Course with a bad level");
        input.LevelId = 987654;

        var error = Fails(await courses.CreateAsync(input, cover: null), ErrorKind.Validation);

        Assert.Equal(nameof(CourseInput.LevelId), error.Field);
    }

    [Fact]
    public async Task Duplicate_titles_get_unique_slugs_and_updates_keep_the_slug()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var first = Ok(await courses.CreateAsync(NewCourse("Twin title"), cover: null));
        var second = Ok(await courses.CreateAsync(NewCourse("Twin title"), cover: null));

        var slugs = await db.Courses.AsNoTracking().Where(c => c.Id == first || c.Id == second).OrderBy(c => c.Id).Select(c => c.Slug).ToListAsync();
        Assert.Equal(new[] { "twin-title", "twin-title-2" }, slugs);

        var update = NewCourse("Twin title renamed", Seed.InstructorId);
        update.Slug = "twin-title-2";
        update.Price = 150;
        Ok(await courses.UpdateAsync(second, update, cover: null, removeCover: false));

        var saved = await db.Courses.AsNoTracking().SingleAsync(c => c.Id == second);
        Assert.Equal("twin-title-2", saved.Slug);
        Assert.Equal(150m, saved.Price);
        Assert.Equal("Twin title renamed", saved.Title.En);
    }

    [Fact]
    public async Task Cover_images_are_validated_and_stored()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();

        using var text = new MemoryStream(Encoding.UTF8.GetBytes("not an image"));
        var error = Fails(await courses.CreateAsync(NewCourse("Course with bad cover"), new FileUpload(text, "cover.exe", text.Length)), ErrorKind.Validation);
        Assert.Equal("cover", error.Field);

        using var png = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var id = Ok(await courses.CreateAsync(NewCourse("Course with cover"), new FileUpload(png, "cover.png", png.Length)));
        var edit = await courses.GetForEditAsync(id);
        Assert.NotNull(edit?.CoverImageKey);
    }

    [Fact]
    public async Task Course_list_counts_students()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();

        var rows = await courses.ListAsync();

        var row = Assert.Single(rows, r => r.Id == Seed.CourseId);
        Assert.True(row.ActiveStudents >= 2);
        Assert.True(row.PendingStudents >= 1);
        Assert.Equal("Ines Instructor", row.InstructorName);
    }

    [Fact]
    public async Task Courses_with_enrollments_cannot_be_deleted()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseAdminService>();

        Fails(await courses.DeleteAsync(Seed.CourseId), ErrorKind.Conflict);
        Fails(await courses.DeleteAsync(999999), ErrorKind.NotFound);
    }

    /// <summary>Foreign keys are RESTRICT, so deleting a course must clean up lessons, quizzes (with questions), assignments, sessions and events itself.</summary>
    [Fact]
    public async Task A_course_with_content_but_no_students_can_be_deleted()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var sp = scope.ServiceProvider;
        var courses = sp.GetRequiredService<ICourseAdminService>();
        var lessons = sp.GetRequiredService<ILessonService>();
        var quizzes = sp.GetRequiredService<IQuizAuthoringService>();
        var sessions = sp.GetRequiredService<IClassSessionService>();
        var db = sp.GetRequiredService<AppDbContext>();

        var courseId = Ok(await courses.CreateAsync(NewCourse("Course to delete", Seed.InstructorId), cover: null));
        Ok(await lessons.CreateAsync(courseId, new LessonInput { Title = "Lesson 1" }, video: null));
        var quizId = Ok(await quizzes.CreateAsync(courseId, new QuizInput { Title = "Quiz" }));
        Ok(await quizzes.SaveQuestionAsync(courseId, quizId, new QuestionInput
        {
            Type = QuestionType.TrueFalse,
            Prompt = "Is this deletable?",
            TrueIsCorrect = true,
        }));
        Ok(await sessions.CreateAsync(courseId, new SessionInput
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(20, 0),
        }));

        Ok(await courses.DeleteAsync(courseId));

        Assert.False(await db.Courses.AnyAsync(c => c.Id == courseId));
        Assert.False(await db.Lessons.AnyAsync(l => l.CourseId == courseId));
        Assert.False(await db.Quizzes.AnyAsync(q => q.CourseId == courseId));
        Assert.False(await db.ClassSessions.AnyAsync(s => s.CourseId == courseId));
    }

    [Fact]
    public async Task Lessons_can_be_created_reordered_edited_and_deleted_by_the_course_instructor()
    {
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        int courseId;
        await using (var setup = Fixture.Scope())
        {
            courseId = Ok(await setup.ServiceProvider.GetRequiredService<ICourseAdminService>().CreateAsync(NewCourse("Lesson playground", Seed.InstructorId), cover: null));
        }

        using var instructor = TestUser.As(Seed.InstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var lessons = scope.ServiceProvider.GetRequiredService<ILessonService>();

        var first = Ok(await lessons.CreateAsync(courseId, new LessonInput { Title = "First", DurationMinutes = 10 }, video: null));
        using var video = new MemoryStream(new byte[2048]);
        var second = Ok(await lessons.CreateAsync(courseId, new LessonInput { Title = "Second" }, new FileUpload(video, "clip.mp4", video.Length)));
        Ok(await lessons.MoveAsync(courseId, second, direction: -1));

        var list = Ok(await lessons.ListAsync(courseId));
        Assert.Equal(new[] { second, first }, list.Lessons.Select(l => l.Id));
        Assert.True(list.Lessons[0].HasUploadedVideo);

        Ok(await lessons.UpdateAsync(courseId, first, new LessonInput { Title = "First (edited)", VideoUrl = "https://vimeo.com/76979871" }, video: null, removeVideo: false));
        var edit = Ok(await lessons.GetForEditAsync(courseId, first));
        Assert.Equal("First (edited)", edit.Input.Title);

        Ok(await lessons.UpdateAsync(courseId, second, new LessonInput { Title = "Second" }, video: null, removeVideo: true));
        Ok(await lessons.DeleteAsync(courseId, first));
        list = Ok(await lessons.ListAsync(courseId));
        var remaining = Assert.Single(list.Lessons);
        Assert.False(remaining.HasUploadedVideo);
    }

    [Fact]
    public async Task Another_instructor_cannot_change_lessons()
    {
        using var _ = TestUser.As(Seed.OtherInstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var lessons = scope.ServiceProvider.GetRequiredService<ILessonService>();

        Fails(await lessons.ListAsync(Seed.CourseId), ErrorKind.Forbidden);
        Fails(await lessons.CreateAsync(Seed.CourseId, new LessonInput { Title = "Intruder" }, video: null), ErrorKind.Forbidden);
        Fails(await lessons.DeleteAsync(Seed.CourseId, Seed.LessonId), ErrorKind.Forbidden);
        Fails(await lessons.MoveAsync(Seed.CourseId, Seed.LessonId, 1), ErrorKind.Forbidden);
    }
}

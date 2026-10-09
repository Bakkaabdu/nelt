using System.Net;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>
/// Opens every page of the site as each kind of user against the real database and views.
/// This is what catches EF Core queries that cannot be translated to SQL, broken Razor views and missing data
/// — failures the compiler cannot see. Each failure lists the page and the exception.
/// </summary>
[Collection(NeltCollection.Name)]
public sealed class PageSmokeTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task Public_pages_render_for_visitors()
    {
        using var client = Fixture.CreateClient();

        var failures = await GetAllAsync(client,
        [
            "/",
            "/?culture=ar",
            "/?culture=de",
            "/?culture=zh",
            "/courses",
            "/courses?language=German",
            $"/courses?level={Seed.LevelA1Id}",
            "/courses?mode=Online",
            $"/courses/{Seed.CourseSlug}",
            $"/courses/{Seed.CourseSlug}?culture=ar",
            $"/courses/{Seed.OtherCourseSlug}",
            $"/courses/{Seed.CourseSlug}/preview/{Seed.PreviewLessonId}",
            "/events",
            "/certificates/verify",
            "/certificates/verify?serial=UNKNOWN-0000",
            "/account/login",
            "/account/register",
            "/health/live",
            "/health/ready",
        ]);

        AssertNoFailures(failures);
    }

    [Fact]
    public async Task Missing_and_unpublished_items_return_404_not_500()
    {
        using var client = Fixture.CreateClient();

        var failures = await GetAllAsync(client,
        [
            "/courses/does-not-exist",
            $"/courses/{Seed.DraftCourseSlug}",
            $"/courses/{Seed.CourseSlug}/preview/{Seed.LessonId}",
            $"/courses/{Seed.CourseSlug}/preview/999999",
            "/media/cover/999999",
            "/files/video/999999",
            "/this/page/does/not/exist",
        ], HttpStatusCode.NotFound);

        AssertNoFailures(failures);
    }

    [Fact]
    public async Task Admin_pages_render()
    {
        var client = await Fixture.ClientForAsync(NeltFixture.AdminEmail);

        var failures = await GetAllAsync(client,
        [
            "/admin",
            "/admin/courses",
            "/admin/courses/create",
            $"/admin/courses/edit/{Seed.CourseId}",
            $"/admin/courses/edit/{Seed.DraftCourseId}",
            "/admin/levels",
            "/admin/levels/create",
            $"/admin/levels/edit/{Seed.LevelA1Id}",
            "/admin/users",
            "/admin/users?role=Student",
            "/admin/users?role=Instructor&q=Ines",
            "/admin/users?q=nelt.test&page=2",
            "/admin/users/create",
            "/admin/users/create?role=Instructor",
            $"/admin/users/edit/{Seed.StudentId}",
            $"/admin/users/edit/{Seed.AdminId}",
            "/admin/enrollments",
            "/admin/enrollments?status=Pending",
            $"/admin/enrollments?course={Seed.CourseId}&q=Sara",
            "/admin/enrollments/create",
            "/admin/enrollments/create?q=Sara",
            "/admin/settings",
            "/admin/devices",
            "/admin/devices/create",
            $"/admin/devices/edit/{Seed.DeviceId}",
            "/admin/devices/punches",
            $"/admin/devices/punches?device={Seed.DeviceId}&outcome=Recorded",
            "/account/password",

            // Admins are staff too: the teaching area must work for every course.
            "/teach",
            $"/teach/courses/{Seed.CourseId}",
            $"/teach/courses/{Seed.OtherCourseId}",
            $"/teach/courses/{Seed.DraftCourseId}",
            "/teach/materials",
            "/teach/events",
            "/teach/events?past=true",
            "/teach/certificates",
            "/teach/certificates?status=Approved",
        ]);

        AssertNoFailures(failures);
    }

    [Fact]
    public async Task Teaching_pages_render_for_the_course_instructor()
    {
        var client = await Fixture.ClientForAsync(Seed.InstructorEmail);
        var course = $"/teach/courses/{Seed.CourseId}";

        var failures = await GetAllAsync(client,
        [
            "/teach",
            course,
            $"{course}/lessons",
            $"{course}/lessons/new",
            $"{course}/lessons/{Seed.LessonId}",
            $"{course}/lessons/{Seed.UnpublishedLessonId}",
            $"{course}/quizzes",
            $"{course}/quizzes/new",
            $"{course}/quizzes/{Seed.QuizId}",
            $"{course}/quizzes/{Seed.QuizId}/questions",
            $"{course}/quizzes/{Seed.QuizId}/results",
            $"{course}/quizzes/{Seed.FinalExamId}/results",
            $"{course}/assignments",
            $"{course}/assignments/new",
            $"{course}/assignments/{Seed.AssignmentId}",
            $"{course}/assignments/{Seed.AssignmentId}/submissions",
            $"{course}/sessions",
            $"{course}/sessions/new",
            $"{course}/sessions/{Seed.PastSessionId}",
            $"{course}/sessions/{Seed.PastSessionId}/edit",
            $"{course}/sessions/{Seed.UpcomingSessionId}",
            $"{course}/attendance",
            $"{course}/students",
            $"{course}/students/{Seed.StudentEnrollmentId}",
            $"{course}/students/{Seed.OnlineEnrollmentId}",
            "/teach/materials",
            $"/teach/materials?level={Seed.LevelA1Id}",
            $"/teach/materials/{Seed.MaterialId}",
            "/teach/events",
            "/teach/events?past=true",
            "/teach/events/new",
            $"/teach/events/{Seed.CourseEventId}",
            "/teach/certificates",
            "/account/password",
        ]);

        AssertNoFailures(failures);
    }

    [Fact]
    public async Task Learning_pages_render_for_an_in_person_student()
    {
        var client = await Fixture.ClientForAsync(Seed.StudentEmail);
        var course = $"/learn/courses/{Seed.CourseId}";

        var failures = await GetAllAsync(client,
        [
            "/learn",
            "/learn/schedule",
            course,
            $"{course}/lessons/{Seed.PreviewLessonId}",
            $"{course}/lessons/{Seed.LessonId}",
            $"{course}/materials",
            $"{course}/attendance",
            $"{course}/assignments",
            $"{course}/assignments/{Seed.AssignmentId}",
            $"{course}/assignments/{Seed.ClosedAssignmentId}",
            $"{course}/quizzes/{Seed.QuizId}",
            $"{course}/quizzes/{Seed.FinalExamId}",
            $"{course}/certificate",
            $"/courses/{Seed.CourseSlug}",
            $"/files/material/{Seed.MaterialId}",
            $"/files/material/{Seed.MaterialId}?download=true",
            "/account/password",
            "/learn?culture=ar",
            "/learn/schedule?culture=zh",
        ]);

        AssertNoFailures(failures);
    }

    [Fact]
    public async Task Learning_pages_render_for_an_online_student()
    {
        var client = await Fixture.ClientForAsync(Seed.OnlineStudentEmail);
        var course = $"/learn/courses/{Seed.CourseId}";

        var failures = await GetAllAsync(client,
        [
            "/learn",
            "/learn/schedule",
            course,
            $"{course}/lessons/{Seed.LessonId}",
            $"{course}/materials",
            $"{course}/attendance",
            $"{course}/assignments",
            $"{course}/assignments/{Seed.AssignmentId}",
            $"{course}/quizzes/{Seed.QuizId}",
            $"{course}/quizzes/{Seed.FinalExamId}",
            $"{course}/certificate",
        ]);

        AssertNoFailures(failures);
    }

    [Fact]
    public async Task Pages_for_a_student_without_courses_render()
    {
        var client = await Fixture.ClientForAsync(Seed.OutsiderEmail);

        var failures = await GetAllAsync(client, ["/learn", "/learn/schedule", "/courses", $"/courses/{Seed.CourseSlug}"]);

        AssertNoFailures(failures);
    }
}

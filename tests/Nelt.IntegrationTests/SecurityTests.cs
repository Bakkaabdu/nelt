using System.Net;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>Who may open what: roles, course ownership, enrollment status, file access and CSRF protection.</summary>
[Collection(NeltCollection.Name)]
public sealed class SecurityTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/courses")]
    [InlineData("/admin/users")]
    [InlineData("/teach")]
    [InlineData("/teach/materials")]
    [InlineData("/learn")]
    [InlineData("/learn/schedule")]
    [InlineData("/account/password")]
    public async Task Visitors_are_sent_to_the_login_page(string path)
    {
        using var client = Fixture.CreateClient();
        using var response = await client.GetAsync(path);

        Assert.True(IsLoginRedirect(response), $"GET {path} returned {(int)response.StatusCode} {response.Headers.Location}");
    }

    [Fact]
    public async Task Students_cannot_open_the_admin_or_teaching_areas()
    {
        var client = await Fixture.ClientForAsync(Seed.StudentEmail);

        foreach (var path in new[] { "/admin", "/admin/courses", "/admin/users", "/teach", $"/teach/courses/{Seed.CourseId}", "/teach/materials" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(IsDenied(response), $"A student opened {path}: {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Instructors_cannot_open_the_admin_area_or_the_learning_area()
    {
        var client = await Fixture.ClientForAsync(Seed.InstructorEmail);

        foreach (var path in new[] { "/admin", "/admin/courses", "/admin/settings", "/learn", $"/learn/courses/{Seed.CourseId}" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(IsDenied(response), $"An instructor opened {path}: {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Instructors_cannot_manage_another_instructors_course()
    {
        var client = await Fixture.ClientForAsync(Seed.OtherInstructorEmail);
        var course = $"/teach/courses/{Seed.CourseId}";

        foreach (var path in new[]
                 {
                     course, $"{course}/lessons", $"{course}/quizzes", $"{course}/quizzes/{Seed.QuizId}/results", $"{course}/assignments",
                     $"{course}/assignments/{Seed.AssignmentId}/submissions", $"{course}/sessions", $"{course}/sessions/{Seed.PastSessionId}",
                     $"{course}/attendance", $"{course}/students", $"{course}/students/{Seed.StudentEnrollmentId}",
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.True(IsDenied(response), $"Another instructor opened {path}: {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task A_student_whose_payment_is_pending_cannot_study_yet()
    {
        var client = await Fixture.ClientForAsync(Seed.PendingStudentEmail);

        using var response = await client.GetAsync($"/learn/courses/{Seed.CourseId}");

        Assert.True(IsDenied(response), $"Pending student got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task A_student_cannot_open_a_course_they_are_not_enrolled_in()
    {
        var client = await Fixture.ClientForAsync(Seed.OutsiderEmail);

        var failures = await GetAllAsync(client,
        [
            $"/learn/courses/{Seed.CourseId}",
            $"/learn/courses/{Seed.CourseId}/lessons/{Seed.LessonId}",
            $"/learn/courses/{Seed.CourseId}/assignments/{Seed.AssignmentId}",
            $"/learn/courses/{Seed.CourseId}/quizzes/{Seed.QuizId}",
            $"/learn/courses/{Seed.CourseId}/certificate",
            $"/files/material/{Seed.MaterialId}",
        ], HttpStatusCode.NotFound);

        AssertNoFailures(failures);
    }

    [Fact]
    public async Task Enrolled_students_can_download_level_materials()
    {
        var client = await Fixture.ClientForAsync(Seed.StudentEmail);

        using var response = await client.GetAsync($"/files/material/{Seed.MaterialId}?download=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("a1-textbook.pdf", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Posts_without_an_antiforgery_token_are_rejected()
    {
        var client = await Fixture.ClientForAsync(NeltFixture.AdminEmail);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Code"] = "X1" });

        using var response = await client.PostAsync("/admin/levels/create", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_does_not_sign_in()
    {
        using var client = Fixture.CreateClient();

        using var response = await NeltFixture.SignInAsync(client, Seed.StudentEmail, "definitely-wrong");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // The form is shown again with an error.
        using var learn = await client.GetAsync("/learn");
        Assert.True(IsLoginRedirect(learn));
    }

    [Fact]
    public async Task Open_redirects_after_sign_in_are_ignored()
    {
        using var client = Fixture.CreateClient();
        var token = await NeltFixture.AntiforgeryTokenAsync(client, "/account/login");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = Seed.OnlineStudentEmail,
            ["Input.Password"] = NeltFixture.Password,
            ["__RequestVerificationToken"] = token,
        });

        using var response = await client.PostAsync("/account/login?returnUrl=https%3A%2F%2Fevil.example%2F", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("evil.example", response.Headers.Location?.OriginalString ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Security_headers_are_sent()
    {
        using var client = Fixture.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.True(response.Headers.Contains("Content-Security-Policy") || response.Content.Headers.Contains("Content-Security-Policy"), "Missing Content-Security-Policy");
        Assert.True(response.Headers.Contains("X-Content-Type-Options"), "Missing X-Content-Type-Options");
    }
}

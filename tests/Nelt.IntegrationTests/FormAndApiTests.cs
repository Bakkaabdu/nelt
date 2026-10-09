using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Common;
using Nelt.Application.Features.Attendance;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>Real browser-style form posts (with antiforgery tokens) and the fingerprint device endpoints.</summary>
[Collection(NeltCollection.Name)]
public sealed class FormAndApiTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    /// <summary>Regression: creating a course with an instructor threw "The LINQ expression ... could not be translated".</summary>
    [Fact]
    public async Task Admin_creates_a_course_with_an_instructor_through_the_form()
    {
        var client = await Fixture.ClientForAsync(NeltFixture.AdminEmail);
        var token = await NeltFixture.AntiforgeryTokenAsync(client, "/admin/courses/create");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.LevelId"] = Seed.LevelA1Id.ToString(CultureInfo.InvariantCulture),
            ["Input.InstructorId"] = Seed.InstructorId.ToString(),
            ["Input.Title.En"] = "Form-created course",
            ["Input.Title.Ar"] = "دورة من النموذج",
            ["Input.Summary.En"] = "Created by the integration tests.",
            ["Input.Price"] = "125.50",
            ["Input.DeliveryMode"] = "Hybrid",
            ["Input.Capacity"] = "12",
            ["Input.SortOrder"] = "5",
            ["Input.IsPublished"] = "false",
            ["Input.Policy.QuizWeight"] = "30",
            ["Input.Policy.AssignmentWeight"] = "20",
            ["Input.Policy.FinalExamWeight"] = "50",
            ["Input.Policy.PassingScore"] = "60",
            ["Input.Policy.ProgressionScore"] = "75",
            ["Input.Policy.MinFinalExamScore"] = "50",
            ["Input.Policy.MinAttendanceRate"] = "75",
        });

        using var response = await client.PostAsync("/admin/courses/create", form);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Expected a redirect after saving, got {(int)response.StatusCode}\n{Html.Excerpt(body)}");

        await using var scope = Fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await db.Courses.AsNoTracking().SingleAsync(c => c.Title.En == "Form-created course");
        Assert.Equal(Seed.InstructorId, course.InstructorId);
        Assert.Equal(125.50m, course.Price);
        Assert.Equal(30, course.Policy.QuizWeight);
        Assert.Equal("form-created-course", course.Slug);
    }

    [Fact]
    public async Task Invalid_course_form_is_shown_again_with_errors()
    {
        var client = await Fixture.ClientForAsync(NeltFixture.AdminEmail);
        var token = await NeltFixture.AntiforgeryTokenAsync(client, "/admin/courses/create");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.LevelId"] = "0",
            ["Input.Title.En"] = string.Empty,
            ["Input.Policy.QuizWeight"] = "90",
        });

        using var response = await client.PostAsync("/admin/courses/create", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_visitor_registers_and_lands_as_a_student()
    {
        using var client = Fixture.CreateClient();
        var token = await NeltFixture.AntiforgeryTokenAsync(client, "/account/register");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.FullName"] = "Rana Registered",
            ["Input.Email"] = "rana.registered@nelt.test",
            ["Input.PhoneNumber"] = "+218 91 000 0000",
            ["Input.Password"] = NeltFixture.Password,
            ["Input.ConfirmPassword"] = NeltFixture.Password,
        });

        using var response = await client.PostAsync("/account/register", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var learn = await client.GetAsync("/learn");
        Assert.Equal(HttpStatusCode.OK, learn.StatusCode);
    }

    [Fact]
    public async Task A_student_reserves_a_seat_through_the_course_page()
    {
        var student = await Fixture.CreateUserAsync(Roles.Student);
        using var client = Fixture.CreateClient();
        using (var signIn = await NeltFixture.SignInAsync(client, student.Email!, NeltFixture.Password))
        {
            Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        }

        var token = await NeltFixture.AntiforgeryTokenAsync(client, $"/courses/{Seed.OtherCourseSlug}");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["mode"] = "Online",
        });

        using var response = await client.PostAsync($"/courses/{Seed.OtherCourseSlug}/enroll", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = Fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enrollment = await db.Enrollments.AsNoTracking().SingleAsync(e => e.StudentId == student.Id && e.CourseId == Seed.OtherCourseId);
        Assert.Equal(EnrollmentStatus.Pending, enrollment.Status);
        Assert.Equal(StudyMode.Online, enrollment.Mode);
    }

    [Fact]
    public async Task Switching_the_language_sets_the_culture_cookie()
    {
        using var client = Fixture.CreateClient();
        var token = await NeltFixture.AntiforgeryTokenAsync(client, "/");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["culture"] = "ar",
            ["returnUrl"] = "/courses",
        });

        using var response = await client.PostAsync("/culture", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var page = await client.GetAsync("/courses");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("dir=\"rtl\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fingerprint_api_records_a_check_in()
    {
        using var client = Fixture.CreateClient();
        var time = Fixture.Factory.Services.GetRequiredService<IPlatformTime>();
        var local = time.ToLocal(DateTime.UtcNow).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/biometric/punches")
        {
            Content = JsonContent.Create(new { punches = new[] { new { userId = SeedData.WalkInBiometricId, time = local } } }),
        };
        request.Headers.Add("X-Device-Key", SeedData.DeviceKey);

        using var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        var result = await response.Content.ReadFromJsonAsync<IngestResult>();
        Assert.NotNull(result);
        Assert.Equal(1, result.Received);
        Assert.Equal(1, result.Recorded + result.Duplicates);

        await using var scope = Fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AttendanceRecords.AnyAsync(r => r.Enrollment!.StudentId == Seed.WalkInStudentId && r.Source == AttendanceSource.Biometric));
    }

    [Fact]
    public async Task Fingerprint_api_rejects_unknown_keys()
    {
        using var client = Fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/biometric/punches")
        {
            Content = JsonContent.Create(new { punches = new[] { new { userId = "1", time = "2026-01-01T10:00:00" } } }),
        };
        request.Headers.Add("X-Device-Key", "nelt_wrong");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Fingerprint_api_rejects_bad_times()
    {
        using var client = Fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/biometric/punches")
        {
            Content = JsonContent.Create(new { punches = new[] { new { userId = "1", time = "not-a-time" } } }),
        };
        request.Headers.Add("X-Device-Key", SeedData.DeviceKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Zkteco_terminal_handshake_and_upload_work()
    {
        using var client = Fixture.CreateClient();

        using var handshake = await client.GetAsync($"/iclock/cdata?SN={SeedData.DeviceSerial}&options=all");
        Assert.Equal(HttpStatusCode.OK, handshake.StatusCode);
        Assert.Contains("GET OPTION FROM", await handshake.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var upload = await client.PostAsync(
            $"/iclock/cdata?SN={SeedData.DeviceSerial}&table=ATTLOG",
            new StringContent("9999\t2026-01-01 10:00:00\t0\t1\nbroken line\n", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Contains("OK", await upload.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var unknown = await client.GetAsync("/iclock/cdata?SN=UNKNOWN");
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
    }
}

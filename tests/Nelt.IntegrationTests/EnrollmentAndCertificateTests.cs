using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Common;
using Nelt.Application.Features.Certificates;
using Nelt.Application.Features.Enrollments;
using Nelt.Application.Features.Quizzes;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>Seat reservations, payment confirmation, capacity, and the certificate flow end to end.</summary>
[Collection(NeltCollection.Name)]
public sealed class EnrollmentAndCertificateTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    private async Task<int> CreateCourseAsync(string title, int? capacity = null, DeliveryMode delivery = DeliveryMode.Hybrid, bool published = true, int? levelId = null)
    {
        await using var scope = Fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new Course
        {
            Slug = $"c-{Guid.NewGuid():N}",
            LevelId = levelId ?? Seed.LevelA1Id,
            InstructorId = Seed.InstructorId,
            Title = LocalizedText.Of(title),
            Capacity = capacity,
            DeliveryMode = delivery,
            IsPublished = published,
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course.Id;
    }

    [Fact]
    public async Task Reserve_confirm_cancel_and_reopen_a_seat()
    {
        var courseId = await CreateCourseAsync("Enrollment flow");
        var studentId = await Fixture.CreateStudentAsync();
        int enrollmentId;

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var enrollments = scope.ServiceProvider.GetRequiredService<IEnrollmentService>();
            enrollmentId = Ok(await enrollments.RequestAsync(courseId, StudyMode.Online));
            Assert.Equal(enrollmentId, Ok(await enrollments.RequestAsync(courseId, StudyMode.Online))); // A double submit keeps one seat.
            Fails(await enrollments.RequestAsync(Seed.DraftCourseId, StudyMode.Online), ErrorKind.NotFound);
        }

        using (TestUser.As(Seed.AdminId, Roles.Admin))
        {
            await using var scope = Fixture.Scope();
            var enrollments = scope.ServiceProvider.GetRequiredService<IEnrollmentService>();

            var pending = await enrollments.ListAsync(new EnrollmentFilter(EnrollmentStatus.Pending, courseId));
            Assert.Contains(pending.Items, e => e.Id == enrollmentId);
            var searched = await enrollments.ListAsync(new EnrollmentFilter(Search: "extra.nelt.test"));
            Assert.Contains(searched.Items, e => e.Id == enrollmentId);

            Ok(await enrollments.ConfirmAsync(enrollmentId, new ConfirmPaymentInput { AmountPaid = 200, PaymentReference = " RCPT-1 " }));
            Ok(await enrollments.ChangeModeAsync(enrollmentId, StudyMode.InPerson));
            Ok(await enrollments.CancelAsync(enrollmentId));
            Fails(await enrollments.ConfirmAsync(999999, new ConfirmPaymentInput()), ErrorKind.NotFound);
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            // A cancelled student can reserve again: the same row is reopened.
            Assert.Equal(enrollmentId, Ok(await scope.ServiceProvider.GetRequiredService<IEnrollmentService>().RequestAsync(courseId, StudyMode.Online)));
        }

        await using (var check = Fixture.Scope())
        {
            var row = await check.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments.AsNoTracking().SingleAsync(e => e.Id == enrollmentId);
            Assert.Equal(EnrollmentStatus.Pending, row.Status);
            Assert.Equal("RCPT-1", row.PaymentReference);
        }
    }

    [Fact]
    public async Task Study_mode_must_match_the_course_delivery()
    {
        var onlineOnly = await CreateCourseAsync("Online only", delivery: DeliveryMode.Online);
        using var _ = TestUser.As(await Fixture.CreateStudentAsync(), Roles.Student);
        await using var scope = Fixture.Scope();

        Fails(await scope.ServiceProvider.GetRequiredService<IEnrollmentService>().RequestAsync(onlineOnly, StudyMode.InPerson), ErrorKind.Validation);
    }

    [Fact]
    public async Task Full_courses_refuse_new_seats_and_reactivating_cancelled_ones()
    {
        var courseId = await CreateCourseAsync("One seat", capacity: 1);
        var first = await Fixture.CreateStudentAsync();
        var second = await Fixture.CreateStudentAsync();
        int firstEnrollment;

        using (TestUser.As(first, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            firstEnrollment = Ok(await scope.ServiceProvider.GetRequiredService<IEnrollmentService>().RequestAsync(courseId, StudyMode.Online));
        }

        using (TestUser.As(Seed.AdminId, Roles.Admin))
        {
            await using var scope = Fixture.Scope();
            Ok(await scope.ServiceProvider.GetRequiredService<IEnrollmentService>().CancelAsync(firstEnrollment));
        }

        using (TestUser.As(second, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            Ok(await scope.ServiceProvider.GetRequiredService<IEnrollmentService>().RequestAsync(courseId, StudyMode.Online)); // Takes the freed seat.
        }

        using (TestUser.As(Seed.AdminId, Roles.Admin))
        {
            await using var scope = Fixture.Scope();
            // Confirming the cancelled seat now would overbook the course.
            Fails(await scope.ServiceProvider.GetRequiredService<IEnrollmentService>().ConfirmAsync(firstEnrollment, new ConfirmPaymentInput()), ErrorKind.Conflict);
        }

        using (TestUser.As(first, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            Fails(await scope.ServiceProvider.GetRequiredService<IEnrollmentService>().RequestAsync(courseId, StudyMode.Online), ErrorKind.Conflict);
        }
    }

    [Fact]
    public async Task Admins_enroll_walk_in_students_but_not_staff()
    {
        var courseId = await CreateCourseAsync("Walk-in course");
        var studentId = await Fixture.CreateStudentAsync();
        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var enrollments = scope.ServiceProvider.GetRequiredService<IEnrollmentService>();

        var id = Ok(await enrollments.EnrollManuallyAsync(new ManualEnrollmentInput { StudentId = studentId, CourseId = courseId, Mode = StudyMode.InPerson, Activate = true, AmountPaid = 100 }));
        Fails(await enrollments.EnrollManuallyAsync(new ManualEnrollmentInput { StudentId = studentId, CourseId = courseId }), ErrorKind.Conflict);

        var staff = Fails(await enrollments.EnrollManuallyAsync(new ManualEnrollmentInput { StudentId = Seed.InstructorId, CourseId = courseId }), ErrorKind.Validation);
        Assert.Equal(nameof(ManualEnrollmentInput.StudentId), staff.Field);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Enrollments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EnrollmentStatus.Active, row.Status);
        Assert.NotNull(row.ActivatedAt);
    }

    [Fact]
    public async Task Certificate_is_requested_approved_printed_and_verifiable()
    {
        // A course whose only graded component is the final exam; the student is online and there are no lessons.
        var courseId = await CreateCourseAsync("Certificate course", published: false);
        var studentId = await Fixture.CreateStudentAsync();
        int enrollmentId;
        await using (var setup = Fixture.Scope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = new Enrollment { CourseId = courseId, StudentId = studentId, Mode = StudyMode.Online, Status = EnrollmentStatus.Active, ActivatedAt = DateTime.UtcNow };
            db.Enrollments.Add(enrollment);
            await db.SaveChangesAsync();
            enrollmentId = enrollment.Id;
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
            var overview = Ok(await certificates.OverviewAsync(courseId));
            Assert.False(overview.Evaluation.IsCertificateEligible); // No final exam result yet.
            Fails(await certificates.RequestAsync(courseId), ErrorKind.Conflict);
        }

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();
            var finalId = Ok(await authoring.CreateAsync(courseId, new QuizInput { Title = "Final", Kind = QuizKind.FinalExam }));
            Ok(await authoring.SaveQuestionAsync(courseId, finalId, new QuestionInput { Type = QuestionType.TrueFalse, Prompt = "Ready?", TrueIsCorrect = true }));
            Ok(await authoring.UpdateAsync(courseId, finalId, new QuizInput { Title = "Final", Kind = QuizKind.FinalExam, IsPublished = true }));
            Ok(await authoring.RecordScoreAsync(courseId, finalId, new RecordScoreInput { EnrollmentId = enrollmentId, Score = 95 }));
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
            var overview = Ok(await certificates.OverviewAsync(courseId));
            Assert.True(overview.Evaluation.IsCertificateEligible, string.Join(", ", overview.Evaluation.Criteria.Select(c => $"{c.Kind}={c.State}")));
            Assert.Equal(95m, overview.Evaluation.OverallScore);
            Assert.Contains(overview.NextLevelCourses, c => c.Id == Seed.OtherCourseId); // The next existing level (A2).

            Ok(await certificates.RequestAsync(courseId));
            Ok(await certificates.RequestAsync(courseId)); // Idempotent.
            Fails(await certificates.MyCertificateAsync(courseId), ErrorKind.NotFound); // Not approved yet.
        }

        int requestId;
        using (TestUser.As(Seed.OtherInstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
            Assert.DoesNotContain(await certificates.RequestsAsync(null), r => r.CourseId == courseId);
            requestId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().CertificateRequests.Where(r => r.EnrollmentId == enrollmentId).Select(r => r.Id).SingleAsync();
            Fails(await certificates.ApproveAsync(requestId), ErrorKind.Forbidden);
        }

        string serial;
        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
            Assert.Contains(await certificates.RequestsAsync(CertificateStatus.Pending), r => r.Id == requestId);
            Ok(await certificates.ApproveAsync(requestId));
            Ok(await certificates.ApproveAsync(requestId)); // Idempotent.
            Fails(await certificates.RejectAsync(requestId, new ReviewInput { Note = "Changed my mind" }), ErrorKind.Conflict);

            var row = Assert.Single(await certificates.RequestsAsync(CertificateStatus.Approved), r => r.Id == requestId);
            serial = row.SerialNumber!;
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var document = Ok(await scope.ServiceProvider.GetRequiredService<ICertificateService>().MyCertificateAsync(courseId));
            Assert.Equal(serial, document.SerialNumber);
            Assert.Equal(95m, document.FinalScore);
        }

        using (TestUser.Anonymous())
        {
            await using var scope = Fixture.Scope();
            var certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
            Assert.NotNull(await certificates.VerifyAsync(serial.ToLowerInvariant()));
            Assert.Null(await certificates.VerifyAsync("NOPE-NOPE-NOPE"));
            Assert.Null(await certificates.VerifyAsync("x"));
        }

        await using (var check = Fixture.Scope())
        {
            var enrollment = await check.ServiceProvider.GetRequiredService<AppDbContext>().Enrollments.AsNoTracking().SingleAsync(e => e.Id == enrollmentId);
            Assert.Equal(EnrollmentStatus.Completed, enrollment.Status);
        }
    }

    [Fact]
    public async Task Instructors_can_decline_a_request_and_students_can_ask_again()
    {
        var courseId = await CreateCourseAsync("Decline course", published: false);
        var studentId = await Fixture.CreateStudentAsync();
        int enrollmentId;
        await using (var setup = Fixture.Scope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = new Enrollment { CourseId = courseId, StudentId = studentId, Mode = StudyMode.Online, Status = EnrollmentStatus.Active, ActivatedAt = DateTime.UtcNow };
            db.Enrollments.Add(enrollment);
            await db.SaveChangesAsync();
            enrollmentId = enrollment.Id;
        }

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();
            var finalId = Ok(await authoring.CreateAsync(courseId, new QuizInput { Title = "Final", Kind = QuizKind.FinalExam }));
            Ok(await authoring.SaveQuestionAsync(courseId, finalId, new QuestionInput { Type = QuestionType.TrueFalse, Prompt = "Q", TrueIsCorrect = true }));
            Ok(await authoring.UpdateAsync(courseId, finalId, new QuizInput { Title = "Final", Kind = QuizKind.FinalExam, IsPublished = true }));
            Ok(await authoring.RecordScoreAsync(courseId, finalId, new RecordScoreInput { EnrollmentId = enrollmentId, Score = 80 }));
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            Ok(await scope.ServiceProvider.GetRequiredService<ICertificateService>().RequestAsync(courseId));
        }

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
            var request = Assert.Single(await certificates.RequestsAsync(CertificateStatus.Pending), r => r.EnrollmentId == enrollmentId);
            Ok(await certificates.RejectAsync(request.Id, new ReviewInput { Note = "Please retake the oral part." }));
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var certificates = scope.ServiceProvider.GetRequiredService<ICertificateService>();
            var overview = Ok(await certificates.OverviewAsync(courseId));
            Assert.Equal(CertificateStatus.Rejected, overview.RequestStatus);
            Assert.Equal("Please retake the oral part.", overview.ReviewNote);

            Ok(await certificates.RequestAsync(courseId));
            Assert.Equal(CertificateStatus.Pending, Ok(await certificates.OverviewAsync(courseId)).RequestStatus);
        }
    }
}

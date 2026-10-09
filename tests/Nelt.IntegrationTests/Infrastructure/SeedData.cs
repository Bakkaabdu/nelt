using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Abstractions;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;

namespace Nelt.IntegrationTests.Infrastructure;

/// <summary>
/// The shared scenario every test can rely on. Tests that change data create their own rows instead of editing these,
/// so the order in which tests run never matters.
/// </summary>
public sealed class SeedData
{
    public const string DeviceSerial = "TESTSN001";
    public const string DeviceKey = "nelt_test-device-key-0123456789";

    // ----- People -----
    public required Guid AdminId { get; init; }
    public required Guid InstructorId { get; init; }
    public required Guid OtherInstructorId { get; init; }
    public required Guid StudentId { get; init; }          // in person, active in Course
    public required Guid OnlineStudentId { get; init; }    // online, active in Course
    public required Guid PendingStudentId { get; init; }   // reserved a seat in Course, not paid yet
    public required Guid OutsiderId { get; init; }         // no enrollment at all
    public required Guid WalkInStudentId { get; init; }    // in person, active in OtherCourse (fingerprint API tests)

    public string InstructorEmail => "instructor@nelt.test";
    public string OtherInstructorEmail => "other.instructor@nelt.test";
    public string StudentEmail => "student@nelt.test";
    public string OnlineStudentEmail => "online.student@nelt.test";
    public string PendingStudentEmail => "pending.student@nelt.test";
    public string OutsiderEmail => "outsider@nelt.test";

    public const string StudentBiometricId = "1001";
    public const string WalkInBiometricId = "1003";

    // ----- Catalogue -----
    public required int LevelA1Id { get; init; }
    public required int LevelA2Id { get; init; }

    /// <summary>Published, hybrid, taught by the instructor, German A1.</summary>
    public required int CourseId { get; init; }
    public required string CourseSlug { get; init; }

    /// <summary>Published, taught by the other instructor, German A2.</summary>
    public required int OtherCourseId { get; init; }
    public required string OtherCourseSlug { get; init; }

    /// <summary>Not published, no instructor.</summary>
    public required int DraftCourseId { get; init; }
    public required string DraftCourseSlug { get; init; }

    public required int PreviewLessonId { get; init; }
    public required int LessonId { get; init; }
    public required int UnpublishedLessonId { get; init; }

    public required int QuizId { get; init; }
    public required int FinalExamId { get; init; }
    public required int AssignmentId { get; init; }
    public required int ClosedAssignmentId { get; init; }
    public required int GradableSubmissionId { get; init; }

    public required int PastSessionId { get; init; }
    public required int UpcomingSessionId { get; init; }
    public required int OpenSessionId { get; init; }

    public required int PublicEventId { get; init; }
    public required int CourseEventId { get; init; }
    public required int MaterialId { get; init; }
    public required int DeviceId { get; init; }

    public required int StudentEnrollmentId { get; init; }
    public required int OnlineEnrollmentId { get; init; }
    public required int PendingEnrollmentId { get; init; }

    public static string HashDeviceKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, string email, string name, string role, string? biometricId = null)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = name,
            BiometricId = biometricId,
        };

        var created = await users.CreateAsync(user, NeltFixture.Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"Could not create {email}: {string.Join("; ", created.Errors.Select(e => e.Description))}");
        }

        var inRole = await users.AddToRoleAsync(user, role);
        if (!inRole.Succeeded)
        {
            throw new InvalidOperationException($"Could not add {email} to {role}.");
        }

        return user;
    }

    public static async Task<SeedData> CreateAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<AppDbContext>();
        var storage = sp.GetRequiredService<IFileStorage>();
        var now = DateTime.UtcNow;

        var admin = await users.FindByEmailAsync(NeltFixture.AdminEmail)
                    ?? throw new InvalidOperationException("The administrator was not seeded. Check the Seed__AdminEmail/Seed__AdminPassword settings.");

        var instructor = await CreateUserAsync(users, "instructor@nelt.test", "Ines Instructor", Roles.Instructor);
        var otherInstructor = await CreateUserAsync(users, "other.instructor@nelt.test", "Otto Other", Roles.Instructor);
        var student = await CreateUserAsync(users, "student@nelt.test", "Sara Student", Roles.Student, StudentBiometricId);
        var online = await CreateUserAsync(users, "online.student@nelt.test", "Omar Online", Roles.Student, "1002");
        var pending = await CreateUserAsync(users, "pending.student@nelt.test", "Paula Pending", Roles.Student);
        var outsider = await CreateUserAsync(users, "outsider@nelt.test", "Olga Outsider", Roles.Student);
        var walkIn = await CreateUserAsync(users, "walkin.student@nelt.test", "Walid WalkIn", Roles.Student, WalkInBiometricId);

        var german = await db.Levels.Where(l => l.Language == TargetLanguage.German).OrderBy(l => l.Rank).ToListAsync();
        if (german.Count < 2)
        {
            throw new InvalidOperationException("The standard German levels were not seeded.");
        }

        var a1 = german[0];
        var a2 = german[1];

        var course = new Course
        {
            Slug = "test-german-a1",
            LevelId = a1.Id,
            InstructorId = instructor.Id,
            Title = LocalizedText.Of("German A1 (test)", "الألمانية A1", "Deutsch A1", "德语 A1"),
            Summary = LocalizedText.Of("Start speaking German."),
            Description = LocalizedText.Of("A complete beginner course."),
            ScheduleNote = LocalizedText.Of("Sun / Tue 18:00"),
            Price = 300,
            DeliveryMode = DeliveryMode.Hybrid,
            Capacity = 30,
            TotalHours = 60,
            StartDate = DateOnly.FromDateTime(now.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(now.AddDays(60)),
            IsPublished = true,
            IsFeatured = true,
        };

        var otherCourse = new Course
        {
            Slug = "test-german-a2",
            LevelId = a2.Id,
            InstructorId = otherInstructor.Id,
            Title = LocalizedText.Of("German A2 (test)"),
            Price = 350,
            DeliveryMode = DeliveryMode.Hybrid,
            IsPublished = true,
        };

        var draft = new Course
        {
            Slug = "test-draft",
            LevelId = a1.Id,
            Title = LocalizedText.Of("Draft course (test)"),
            DeliveryMode = DeliveryMode.Online,
            IsPublished = false,
        };

        db.Courses.AddRange(course, otherCourse, draft);
        await db.SaveChangesAsync();

        var preview = new Lesson { CourseId = course.Id, Title = "Welcome (preview)", SortOrder = 1, IsPreview = true, IsPublished = true, DurationMinutes = 5 };
        var lesson = new Lesson { CourseId = course.Id, Title = "Greetings", SortOrder = 2, IsPublished = true, DurationMinutes = 20, VideoUrl = "https://youtu.be/dQw4w9WgXcQ" };
        var hidden = new Lesson { CourseId = course.Id, Title = "Not ready yet", SortOrder = 3, IsPublished = false };
        db.Lessons.AddRange(preview, lesson, hidden);

        var quiz = new Quiz { CourseId = course.Id, Title = "Greetings quiz", Kind = QuizKind.Quiz, MaxAttempts = 3, IsPublished = true };
        var single = new Question { Type = QuestionType.SingleChoice, Prompt = "How do you say hello?", Points = 2, SortOrder = 1 };
        single.Options.Add(new QuestionOption { Text = "Hallo", IsCorrect = true, SortOrder = 1 });
        single.Options.Add(new QuestionOption { Text = "Tschüss", IsCorrect = false, SortOrder = 2 });
        var trueFalse = new Question { Type = QuestionType.TrueFalse, Prompt = "\"Danke\" means thank you.", Points = 1, SortOrder = 2 };
        trueFalse.Options.Add(new QuestionOption { Text = "True", IsCorrect = true, SortOrder = 1 });
        trueFalse.Options.Add(new QuestionOption { Text = "False", IsCorrect = false, SortOrder = 2 });
        quiz.Questions.Add(single);
        quiz.Questions.Add(trueFalse);

        var final = new Quiz { CourseId = course.Id, Title = "Final exam", Kind = QuizKind.FinalExam, MaxAttempts = 1, IsPublished = true };
        var finalQuestion = new Question { Type = QuestionType.SingleChoice, Prompt = "Ich ___ Sara.", Points = 1, SortOrder = 1 };
        finalQuestion.Options.Add(new QuestionOption { Text = "bin", IsCorrect = true, SortOrder = 1 });
        finalQuestion.Options.Add(new QuestionOption { Text = "bist", IsCorrect = false, SortOrder = 2 });
        final.Questions.Add(finalQuestion);
        db.Quizzes.AddRange(quiz, final);

        var assignment = new Assignment { CourseId = course.Id, Title = "Introduce yourself", Instructions = "Write five sentences.", DueAt = now.AddDays(7), MaxScore = 20, IsPublished = true };
        var closed = new Assignment { CourseId = course.Id, Title = "Closed homework", Instructions = "Too late now.", DueAt = now.AddDays(-2), AllowLateSubmissions = false, IsPublished = true };
        db.Assignments.AddRange(assignment, closed);

        var past = new ClassSession { CourseId = course.Id, StartsAt = now.AddDays(-1), EndsAt = now.AddDays(-1).AddHours(2), Topic = "Alphabet", Room = "R1" };
        var upcoming = new ClassSession { CourseId = course.Id, StartsAt = now.AddDays(1), EndsAt = now.AddDays(1).AddHours(2), Topic = "Numbers", Room = "R1" };
        var open = new ClassSession { CourseId = course.Id, StartsAt = now.AddMinutes(-5), EndsAt = now.AddMinutes(115), Topic = "Today", Room = "R1" };
        var otherOpen = new ClassSession { CourseId = otherCourse.Id, StartsAt = now.AddMinutes(-5), EndsAt = now.AddMinutes(115), Topic = "Today (A2)", Room = "R2" };
        db.ClassSessions.AddRange(past, upcoming, open, otherOpen);

        var publicEvent = new CourseEvent
        {
            Type = EventType.CulturalEvent,
            Title = LocalizedText.Of("Oktoberfest evening"),
            Description = LocalizedText.Of("Music and food."),
            Location = "Main hall",
            StartsAt = now.AddDays(5),
            EndsAt = now.AddDays(5).AddHours(3),
            IsPublic = true,
            IsPublished = true,
            CreatedById = admin.Id,
        };
        var courseEvent = new CourseEvent
        {
            CourseId = course.Id,
            Type = EventType.ConversationSession,
            Title = LocalizedText.Of("Conversation club"),
            Description = LocalizedText.Of("Practice speaking."),
            StartsAt = now.AddDays(3),
            IsPublic = false,
            IsPublished = true,
            CreatedById = instructor.Id,
        };
        db.Events.AddRange(publicEvent, courseEvent);

        using var pdf = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 test file"));
        var fileKey = await storage.SaveAsync(pdf, "materials", ".pdf");
        var material = new LearningMaterial
        {
            LevelId = a1.Id,
            Type = MaterialType.Book,
            Title = "A1 textbook",
            FileKey = fileKey,
            FileName = "a1-textbook.pdf",
            ContentType = "application/pdf",
            SizeBytes = pdf.Length,
            SortOrder = 1,
            IsPublished = true,
            UploadedById = instructor.Id,
        };
        db.Materials.Add(material);

        var studentEnrollment = new Enrollment { CourseId = course.Id, StudentId = student.Id, Mode = StudyMode.InPerson, Status = EnrollmentStatus.Active, ActivatedAt = now.AddDays(-20), AmountPaid = 300 };
        var onlineEnrollment = new Enrollment { CourseId = course.Id, StudentId = online.Id, Mode = StudyMode.Online, Status = EnrollmentStatus.Active, ActivatedAt = now.AddDays(-20), AmountPaid = 300 };
        var pendingEnrollment = new Enrollment { CourseId = course.Id, StudentId = pending.Id, Mode = StudyMode.InPerson, Status = EnrollmentStatus.Pending };
        var walkInEnrollment = new Enrollment { CourseId = otherCourse.Id, StudentId = walkIn.Id, Mode = StudyMode.InPerson, Status = EnrollmentStatus.Active, ActivatedAt = now.AddDays(-10) };
        db.Enrollments.AddRange(studentEnrollment, onlineEnrollment, pendingEnrollment, walkInEnrollment);

        var device = new BiometricDevice { Name = "Front door", SerialNumber = DeviceSerial, ApiKeyHash = HashDeviceKey(DeviceKey), Location = "Entrance", IsActive = true };
        db.BiometricDevices.Add(device);

        await db.SaveChangesAsync();

        var submission = new Submission { AssignmentId = assignment.Id, EnrollmentId = onlineEnrollment.Id, Text = "Ich heiße Omar.", SubmittedAt = now.AddHours(-3) };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        return new SeedData
        {
            AdminId = admin.Id,
            InstructorId = instructor.Id,
            OtherInstructorId = otherInstructor.Id,
            StudentId = student.Id,
            OnlineStudentId = online.Id,
            PendingStudentId = pending.Id,
            OutsiderId = outsider.Id,
            WalkInStudentId = walkIn.Id,
            LevelA1Id = a1.Id,
            LevelA2Id = a2.Id,
            CourseId = course.Id,
            CourseSlug = course.Slug,
            OtherCourseId = otherCourse.Id,
            OtherCourseSlug = otherCourse.Slug,
            DraftCourseId = draft.Id,
            DraftCourseSlug = draft.Slug,
            PreviewLessonId = preview.Id,
            LessonId = lesson.Id,
            UnpublishedLessonId = hidden.Id,
            QuizId = quiz.Id,
            FinalExamId = final.Id,
            AssignmentId = assignment.Id,
            ClosedAssignmentId = closed.Id,
            GradableSubmissionId = submission.Id,
            PastSessionId = past.Id,
            UpcomingSessionId = upcoming.Id,
            OpenSessionId = open.Id,
            PublicEventId = publicEvent.Id,
            CourseEventId = courseEvent.Id,
            MaterialId = material.Id,
            DeviceId = device.Id,
            StudentEnrollmentId = studentEnrollment.Id,
            OnlineEnrollmentId = onlineEnrollment.Id,
            PendingEnrollmentId = pendingEnrollment.Id,
        };
    }
}

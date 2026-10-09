using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Common;
using Nelt.Application.Features.Assignments;
using Nelt.Application.Features.Courses;
using Nelt.Application.Features.Quizzes;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;
using Nelt.IntegrationTests.Infrastructure;

namespace Nelt.IntegrationTests;

/// <summary>Quizzes (authoring, taking, grading, attempts, final exam rules) and assignments (submit, grade).</summary>
[Collection(NeltCollection.Name)]
public sealed class AssessmentServiceTests(NeltFixture fixture) : IntegrationTest(fixture)
{
    /// <summary>A fresh course taught by the instructor, with one active in-person and one active online student.</summary>
    private async Task<(int CourseId, Guid InPersonId, Guid OnlineId)> CourseWithStudentsAsync(string title)
    {
        var inPerson = await Fixture.CreateStudentAsync();
        var online = await Fixture.CreateStudentAsync();

        using var _ = TestUser.As(Seed.AdminId, Roles.Admin);
        await using var scope = Fixture.Scope();
        var courseId = Ok(await scope.ServiceProvider.GetRequiredService<ICourseAdminService>().CreateAsync(new CourseInput
        {
            LevelId = Seed.LevelA1Id,
            InstructorId = Seed.InstructorId,
            Title = LocalizedText.Of(title),
            DeliveryMode = DeliveryMode.Hybrid,
        }, cover: null));

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Enrollments.Add(new Enrollment { CourseId = courseId, StudentId = inPerson, Mode = StudyMode.InPerson, Status = EnrollmentStatus.Active, ActivatedAt = DateTime.UtcNow });
        db.Enrollments.Add(new Enrollment { CourseId = courseId, StudentId = online, Mode = StudyMode.Online, Status = EnrollmentStatus.Active, ActivatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (courseId, inPerson, online);
    }

    private static QuestionInput SingleChoice(string prompt, int points = 1) => new()
    {
        Type = QuestionType.SingleChoice,
        Prompt = prompt,
        Points = points,
        Options =
        [
            new OptionInput { Text = "Right", IsCorrect = true },
            new OptionInput { Text = "Wrong", IsCorrect = false },
            new OptionInput { Text = "   " }, // Blank rows from the form are ignored.
        ],
    };

    [Fact]
    public async Task Instructor_builds_a_quiz_and_a_student_takes_it()
    {
        var (courseId, studentId, _) = await CourseWithStudentsAsync("Quiz course");
        int quizId;

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();

            Fails(await authoring.CreateAsync(courseId, new QuizInput { Title = "Too early", IsPublished = true }), ErrorKind.Validation);

            quizId = Ok(await authoring.CreateAsync(courseId, new QuizInput { Title = "Week 1", MaxAttempts = 2, TimeLimitMinutes = 15 }));
            Fails(await authoring.UpdateAsync(courseId, quizId, new QuizInput { Title = "Week 1", MaxAttempts = 2, IsPublished = true }), ErrorKind.Validation);

            Ok(await authoring.SaveQuestionAsync(courseId, quizId, SingleChoice("Pick the right one", points: 3)));
            Ok(await authoring.SaveQuestionAsync(courseId, quizId, new QuestionInput { Type = QuestionType.TrueFalse, Prompt = "True?", TrueIsCorrect = true }));
            Ok(await authoring.UpdateAsync(courseId, quizId, new QuizInput { Title = "Week 1", MaxAttempts = 2, TimeLimitMinutes = 15, IsPublished = true }));

            var questions = Ok(await authoring.QuestionsAsync(courseId, quizId));
            Assert.Equal(2, questions.Questions.Count);
            Assert.Equal(4, questions.TotalPoints);
            Assert.Equal(2, questions.Questions[0].Options.Count);
            Assert.False(questions.IsLocked);
        }

        int attemptId;
        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var taking = scope.ServiceProvider.GetRequiredService<IQuizTakingService>();

            var intro = Ok(await taking.IntroAsync(courseId, quizId));
            Assert.Equal(QuizBlockReason.None, intro.BlockReason);
            Assert.Equal(2, intro.QuestionCount);

            attemptId = Ok(await taking.StartAsync(courseId, quizId));
            Assert.Equal(attemptId, Ok(await taking.StartAsync(courseId, quizId))); // Starting again resumes the open attempt.

            var sheet = Ok(await taking.SheetAsync(courseId, attemptId));
            Assert.NotNull(sheet.DeadlineAt);
            var answers = new Dictionary<int, int[]>();
            foreach (var question in sheet.Questions)
            {
                // First option is the correct one for both questions ("Right" / "True").
                answers[question.Id] = [question.Options[0].Id];
            }

            Ok(await taking.SubmitAsync(courseId, attemptId, answers));
            Ok(await taking.SubmitAsync(courseId, attemptId, answers)); // A double click must not fail.

            var review = Ok(await taking.ReviewAsync(courseId, attemptId));
            Assert.Equal(100m, review.ScorePercent);
            Assert.True(review.Passed);
            Assert.True(review.CanRetry);
            Assert.False(review.AnswersRevealed); // Answers stay hidden while attempts remain.

            var second = Ok(await taking.StartAsync(courseId, quizId));
            Ok(await taking.SubmitAsync(courseId, second, new Dictionary<int, int[]>()));
            var secondReview = Ok(await taking.ReviewAsync(courseId, second));
            Assert.Equal(0m, secondReview.ScorePercent);
            Assert.True(secondReview.AnswersRevealed);

            Fails(await taking.StartAsync(courseId, quizId), ErrorKind.Conflict); // No attempts left.
            var after = Ok(await taking.IntroAsync(courseId, quizId));
            Assert.Equal(QuizBlockReason.NoAttemptsLeft, after.BlockReason);
            Assert.Equal(100m, after.BestScore);
        }

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();

            var list = Ok(await authoring.ListAsync(courseId));
            var row = Assert.Single(list.Quizzes);
            Assert.Equal(2, row.QuestionCount);
            Assert.Equal(2, row.Attempts);
            Assert.Equal(50m, row.AverageScore);

            var results = Ok(await authoring.ResultsAsync(courseId, quizId));
            var student = Assert.Single(results.Rows, r => r.Attempts == 2);
            Assert.Equal(100m, student.BestScore);

            // Questions are locked once students have taken the quiz, and the quiz cannot be deleted.
            Fails(await authoring.SaveQuestionAsync(courseId, quizId, SingleChoice("Late change")), ErrorKind.Conflict);
            Fails(await authoring.DeleteAsync(courseId, quizId), ErrorKind.Conflict);
            Assert.True(Ok(await authoring.QuestionsAsync(courseId, quizId)).IsLocked);
        }
    }

    [Fact]
    public async Task Answers_to_options_of_other_questions_are_ignored()
    {
        var (courseId, studentId, _) = await CourseWithStudentsAsync("Tamper course");
        int quizId;
        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();
            quizId = Ok(await authoring.CreateAsync(courseId, new QuizInput { Title = "Tamper", MaxAttempts = 1 }));
            Ok(await authoring.SaveQuestionAsync(courseId, quizId, SingleChoice("Q1")));
            Ok(await authoring.UpdateAsync(courseId, quizId, new QuizInput { Title = "Tamper", MaxAttempts = 1, IsPublished = true }));
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var taking = scope.ServiceProvider.GetRequiredService<IQuizTakingService>();
            var attemptId = Ok(await taking.StartAsync(courseId, quizId));
            var sheet = Ok(await taking.SheetAsync(courseId, attemptId));
            var question = Assert.Single(sheet.Questions);

            // An option id from the seeded quiz in another course, plus a made-up one.
            Ok(await taking.SubmitAsync(courseId, attemptId, new Dictionary<int, int[]> { [question.Id] = [int.MaxValue, 1] }));

            Assert.Equal(0m, Ok(await taking.ReviewAsync(courseId, attemptId)).ScorePercent);
        }
    }

    [Fact]
    public async Task Quizzes_for_one_study_mode_are_hidden_from_the_other()
    {
        var (courseId, _, onlineId) = await CourseWithStudentsAsync("Audience course");
        int quizId;
        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();
            quizId = Ok(await authoring.CreateAsync(courseId, new QuizInput { Title = "Classroom only", Audience = QuizAudience.InPerson }));
            Ok(await authoring.SaveQuestionAsync(courseId, quizId, SingleChoice("Q")));
            Ok(await authoring.UpdateAsync(courseId, quizId, new QuizInput { Title = "Classroom only", Audience = QuizAudience.InPerson, IsPublished = true }));
        }

        using (TestUser.As(onlineId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            Fails(await scope.ServiceProvider.GetRequiredService<IQuizTakingService>().IntroAsync(courseId, quizId), ErrorKind.Forbidden);
        }
    }

    [Fact]
    public async Task Online_students_must_finish_all_lessons_before_the_final_exam()
    {
        using var _ = TestUser.As(Seed.OnlineStudentId, Roles.Student);
        await using var scope = Fixture.Scope();
        var taking = scope.ServiceProvider.GetRequiredService<IQuizTakingService>();

        // The seeded online student has not completed the seeded lessons.
        var intro = Ok(await taking.IntroAsync(Seed.CourseId, Seed.FinalExamId));

        Assert.Equal(QuizBlockReason.LessonsIncomplete, intro.BlockReason);
        Fails(await taking.StartAsync(Seed.CourseId, Seed.FinalExamId), ErrorKind.Conflict);
    }

    [Fact]
    public async Task Only_one_final_exam_per_audience_is_allowed()
    {
        using var _ = TestUser.As(Seed.InstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();

        var error = Fails(await authoring.CreateAsync(Seed.CourseId, new QuizInput { Title = "Second final", Kind = QuizKind.FinalExam }), ErrorKind.Validation);

        Assert.Equal(nameof(QuizInput.Kind), error.Field);
    }

    [Fact]
    public async Task Instructors_record_paper_quiz_scores()
    {
        var (courseId, inPersonId, _) = await CourseWithStudentsAsync("Paper quiz course");
        using var _ = TestUser.As(Seed.InstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enrollmentId = await db.Enrollments.Where(e => e.CourseId == courseId && e.StudentId == inPersonId).Select(e => e.Id).SingleAsync();
        var quizId = Ok(await authoring.CreateAsync(courseId, new QuizInput { Title = "Paper test" }));

        Ok(await authoring.RecordScoreAsync(courseId, quizId, new RecordScoreInput { EnrollmentId = enrollmentId, Score = 87.456m, Note = "Paper" }));
        Ok(await authoring.RecordScoreAsync(courseId, quizId, new RecordScoreInput { EnrollmentId = enrollmentId, Score = 90m })); // Updates, not duplicates.

        var results = Ok(await authoring.ResultsAsync(courseId, quizId));
        var row = Assert.Single(results.Rows, r => r.EnrollmentId == enrollmentId);
        Assert.True(row.HasRecordedScore);
        Assert.Equal(90m, row.BestScore);
        Fails(await authoring.RecordScoreAsync(courseId, quizId, new RecordScoreInput { EnrollmentId = Seed.StudentEnrollmentId, Score = 50 }), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Other_instructors_cannot_author_quizzes_in_foreign_courses()
    {
        using var _ = TestUser.As(Seed.OtherInstructorId, Roles.Instructor);
        await using var scope = Fixture.Scope();
        var authoring = scope.ServiceProvider.GetRequiredService<IQuizAuthoringService>();

        Fails(await authoring.ListAsync(Seed.CourseId), ErrorKind.Forbidden);
        Fails(await authoring.CreateAsync(Seed.CourseId, new QuizInput { Title = "Nope" }), ErrorKind.Forbidden);
        Fails(await authoring.ResultsAsync(Seed.CourseId, Seed.QuizId), ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Students_submit_assignments_and_instructors_grade_them()
    {
        var (courseId, studentId, _) = await CourseWithStudentsAsync("Assignment course");
        int assignmentId;
        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var assignments = scope.ServiceProvider.GetRequiredService<IAssignmentService>();
            using var attachment = new MemoryStream(Encoding.UTF8.GetBytes("worksheet"));
            assignmentId = Ok(await assignments.CreateAsync(courseId, new AssignmentInput
            {
                Title = "Essay",
                Instructions = "Write about your family.",
                DueAt = DateTime.UtcNow.AddDays(5),
                MaxScore = 10,
            }, new FileUpload(attachment, "worksheet.txt", attachment.Length)));

            var edit = Ok(await assignments.GetForEditAsync(courseId, assignmentId));
            Assert.Equal("worksheet.txt", edit.AttachmentName);
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var student = scope.ServiceProvider.GetRequiredService<IStudentAssignmentService>();

            Fails(await student.SubmitAsync(courseId, assignmentId, new SubmitInput { Text = "  " }, file: null), ErrorKind.Validation);
            Ok(await student.SubmitAsync(courseId, assignmentId, new SubmitInput { Text = "Meine Familie ist groß." }, file: null));
            using var file = new MemoryStream(Encoding.UTF8.GetBytes("essay"));
            Ok(await student.SubmitAsync(courseId, assignmentId, new SubmitInput { Text = "Updated." }, new FileUpload(file, "essay.txt", file.Length)));

            var detail = Ok(await student.GetAsync(courseId, assignmentId));
            Assert.Equal(StudentAssignmentState.Submitted, detail.State);
            Assert.Equal("Updated.", detail.Submission?.Text);
            Assert.Equal("essay.txt", detail.Submission?.FileName);
            Assert.True(detail.CanSubmit);
        }

        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            var assignments = scope.ServiceProvider.GetRequiredService<IAssignmentService>();

            var submissions = Ok(await assignments.SubmissionsAsync(courseId, assignmentId));
            var row = Assert.Single(submissions.Rows, r => r.SubmissionId is not null);
            Assert.Equal(2, submissions.Rows.Count); // Students without a submission are listed too.

            Fails(await assignments.GradeAsync(courseId, row.SubmissionId!.Value, new GradeInput { Score = 11 }), ErrorKind.Validation);
            Ok(await assignments.GradeAsync(courseId, row.SubmissionId.Value, new GradeInput { Score = 9, Feedback = "Sehr gut" }));
            Fails(await assignments.DeleteAsync(courseId, assignmentId), ErrorKind.Conflict);

            var list = Ok(await assignments.ListAsync(courseId));
            var listed = Assert.Single(list.Assignments);
            Assert.Equal(1, listed.Graded);
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            var student = scope.ServiceProvider.GetRequiredService<IStudentAssignmentService>();

            var mine = Ok(await student.ListAsync(courseId));
            Assert.Equal(StudentAssignmentState.Graded, Assert.Single(mine.Assignments).State);
            Fails(await student.SubmitAsync(courseId, assignmentId, new SubmitInput { Text = "Too late to change" }, file: null), ErrorKind.Conflict);
        }
    }

    [Fact]
    public async Task Closed_deadlines_and_pending_enrollments_block_submissions()
    {
        await using var scope = Fixture.Scope();
        var student = scope.ServiceProvider.GetRequiredService<IStudentAssignmentService>();

        using (TestUser.As(Seed.StudentId, Roles.Student))
        {
            Fails(await student.SubmitAsync(Seed.CourseId, Seed.ClosedAssignmentId, new SubmitInput { Text = "Late" }, file: null), ErrorKind.Conflict);
            var detail = Ok(await student.GetAsync(Seed.CourseId, Seed.ClosedAssignmentId));
            Assert.Equal(StudentAssignmentState.Closed, detail.State);
            Assert.False(detail.CanSubmit);
        }

        using (TestUser.As(Seed.PendingStudentId, Roles.Student))
        {
            Fails(await student.SubmitAsync(Seed.CourseId, Seed.AssignmentId, new SubmitInput { Text = "Hi" }, file: null), ErrorKind.Forbidden);
        }
    }

    [Fact]
    public async Task Disallowed_upload_types_are_rejected()
    {
        var (courseId, studentId, _) = await CourseWithStudentsAsync("Upload rules course");
        int assignmentId;
        using (TestUser.As(Seed.InstructorId, Roles.Instructor))
        {
            await using var scope = Fixture.Scope();
            assignmentId = Ok(await scope.ServiceProvider.GetRequiredService<IAssignmentService>().CreateAsync(courseId,
                new AssignmentInput { Title = "Upload", Instructions = "Upload a file." }, attachment: null));
        }

        using (TestUser.As(studentId, Roles.Student))
        {
            await using var scope = Fixture.Scope();
            using var script = new MemoryStream(Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
            var error = Fails(await scope.ServiceProvider.GetRequiredService<IStudentAssignmentService>()
                .SubmitAsync(courseId, assignmentId, new SubmitInput(), new FileUpload(script, "evil.html", script.Length)), ErrorKind.Validation);
            Assert.Equal("file", error.Field);
        }
    }
}

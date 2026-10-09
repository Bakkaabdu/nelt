using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Quizzes;

public interface IQuizAuthoringService
{
    Task<Result<QuizList>> ListAsync(int courseId, CancellationToken ct = default);
    Task<Result<QuizEditModel>> GetForEditAsync(int courseId, int? quizId, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(int courseId, QuizInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(int courseId, int quizId, QuizInput input, CancellationToken ct = default);
    Task<Result> DeleteAsync(int courseId, int quizId, CancellationToken ct = default);
    Task<Result<QuizQuestionsModel>> QuestionsAsync(int courseId, int quizId, CancellationToken ct = default);
    Task<Result> SaveQuestionAsync(int courseId, int quizId, QuestionInput input, CancellationToken ct = default);
    Task<Result> DeleteQuestionAsync(int courseId, int quizId, int questionId, CancellationToken ct = default);
    Task<Result<QuizResultsModel>> ResultsAsync(int courseId, int quizId, CancellationToken ct = default);
    Task<Result> RecordScoreAsync(int courseId, int quizId, RecordScoreInput input, CancellationToken ct = default);
}

internal sealed class QuizAuthoringService(IAppDbContext db, ICourseAccess access, ICurrentUser user, IPlatformTime time) : IQuizAuthoringService
{
    public async Task<Result<QuizList>> ListAsync(int courseId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var quizzes = await db.Quizzes.AsNoTracking().Where(q => q.CourseId == courseId)
            .OrderBy(q => q.Kind).ThenBy(q => q.CreatedAt)
            .Select(q => new QuizRow(
                q.Id, q.Title, q.Kind, q.Audience,
                q.Questions.Count, q.Questions.Sum(x => (int?)x.Points) ?? 0,
                q.Attempts.Count(a => a.SubmittedAt != null),
                q.Attempts.Where(a => a.ScorePercent != null).Average(a => a.ScorePercent),
                q.IsPublished, q.AvailableFrom, q.AvailableUntil))
            .ToListAsync(ct);

        return new QuizList(course.Value, quizzes);
    }

    public async Task<Result<QuizEditModel>> GetForEditAsync(int courseId, int? quizId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var lessons = await LessonsAsync(courseId, ct);
        if (quizId is null)
        {
            return new QuizEditModel(course.Value, null, new QuizInput(), lessons, false);
        }

        var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quizId && q.CourseId == courseId, ct);
        if (quiz is null)
        {
            return Error.NotFound();
        }

        var input = new QuizInput
        {
            Title = quiz.Title,
            Instructions = quiz.Instructions,
            Kind = quiz.Kind,
            Audience = quiz.Audience,
            LessonId = quiz.LessonId,
            TimeLimitMinutes = quiz.TimeLimitMinutes,
            PassingScore = quiz.PassingScore,
            MaxAttempts = quiz.MaxAttempts,
            AvailableFrom = quiz.AvailableFrom is { } from ? time.ToLocal(from) : null,
            AvailableUntil = quiz.AvailableUntil is { } until ? time.ToLocal(until) : null,
            IsPublished = quiz.IsPublished,
        };

        var hasAttempts = await db.QuizAttempts.AnyAsync(a => a.QuizId == quiz.Id, ct);
        return new QuizEditModel(course.Value, quiz.Id, input, lessons, hasAttempts);
    }

    public async Task<Result<int>> CreateAsync(int courseId, QuizInput input, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        if (input.IsPublished)
        {
            return Error.Validation("Add questions before publishing the quiz.", nameof(QuizInput.IsPublished));
        }

        var quiz = new Quiz { CourseId = courseId };
        if (await ApplyAsync(quiz, input, ct) is { } error)
        {
            return error;
        }

        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);
        return quiz.Id;
    }

    public async Task<Result> UpdateAsync(int courseId, int quizId, QuizInput input, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId && q.CourseId == courseId, ct);
        if (quiz is null)
        {
            return Error.NotFound();
        }

        if (input.IsPublished && !await db.Questions.AnyAsync(q => q.QuizId == quizId, ct))
        {
            return Error.Validation("Add questions before publishing the quiz.", nameof(QuizInput.IsPublished));
        }

        if (await ApplyAsync(quiz, input, ct) is { } error)
        {
            return error;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int courseId, int quizId, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId && q.CourseId == courseId, ct);
        if (quiz is null)
        {
            return Error.NotFound();
        }

        if (await db.QuizAttempts.AnyAsync(a => a.QuizId == quizId, ct))
        {
            return Error.Conflict("Students already took this quiz. Unpublish it instead of deleting it.");
        }

        db.Quizzes.Remove(quiz);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<QuizQuestionsModel>> QuestionsAsync(int courseId, int quizId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var quiz = await db.Quizzes.AsNoTracking().Where(q => q.Id == quizId && q.CourseId == courseId)
            .Select(q => new { q.Id, q.Title, q.Kind, Locked = q.Attempts.Any() })
            .FirstOrDefaultAsync(ct);
        if (quiz is null)
        {
            return Error.NotFound();
        }

        var questions = await db.Questions.AsNoTracking().Where(q => q.QuizId == quizId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .Select(q => new QuestionView(q.Id, q.Type, q.Prompt, q.Points, q.SortOrder,
                q.Options.OrderBy(o => o.SortOrder).Select(o => new OptionView(o.Id, o.Text, o.IsCorrect)).ToList()))
            .ToListAsync(ct);

        return new QuizQuestionsModel(course.Value, quiz.Id, quiz.Title, quiz.Kind, quiz.Locked, questions);
    }

    public async Task<Result> SaveQuestionAsync(int courseId, int quizId, QuestionInput input, CancellationToken ct = default)
    {
        var guard = await EditableQuizAsync(courseId, quizId, ct);
        if (guard.Failed)
        {
            return guard;
        }

        Question question;
        if (input.Id is { } questionId)
        {
            var existing = await db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == questionId && q.QuizId == quizId, ct);
            if (existing is null)
            {
                return Error.NotFound();
            }

            question = existing;
            db.QuestionOptions.RemoveRange(question.Options);
            question.Options.Clear();
        }
        else
        {
            var order = await db.Questions.Where(q => q.QuizId == quizId).MaxAsync(q => (int?)q.SortOrder, ct) ?? 0;
            question = new Question { QuizId = quizId, SortOrder = order + 1 };
            db.Questions.Add(question);
        }

        question.Type = input.Type;
        question.Prompt = input.Prompt.Trim();
        question.Points = input.Points;

        if (input.Type == QuestionType.TrueFalse)
        {
            question.Options.Add(new QuestionOption { Text = "True", IsCorrect = input.TrueIsCorrect, SortOrder = 1 });
            question.Options.Add(new QuestionOption { Text = "False", IsCorrect = !input.TrueIsCorrect, SortOrder = 2 });
        }
        else
        {
            var order = 1;
            foreach (var option in input.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)))
            {
                question.Options.Add(new QuestionOption { Text = option.Text!.Trim(), IsCorrect = option.IsCorrect, SortOrder = order++ });
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteQuestionAsync(int courseId, int quizId, int questionId, CancellationToken ct = default)
    {
        var guard = await EditableQuizAsync(courseId, quizId, ct);
        if (guard.Failed)
        {
            return guard;
        }

        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId && q.QuizId == quizId, ct);
        if (question is null)
        {
            return Error.NotFound();
        }

        db.Questions.Remove(question);
        await db.SaveChangesAsync(ct);

        if (!await db.Questions.AnyAsync(q => q.QuizId == quizId, ct))
        {
            await db.Quizzes.Where(q => q.Id == quizId).ExecuteUpdateAsync(s => s.SetProperty(q => q.IsPublished, false), ct);
        }

        return Result.Success();
    }

    public async Task<Result<QuizResultsModel>> ResultsAsync(int courseId, int quizId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quizId && q.CourseId == courseId, ct);
        if (quiz is null)
        {
            return Error.NotFound();
        }

        var rows = (await db.Enrollments.AsNoTracking()
                .Where(e => e.CourseId == courseId && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed))
                .OrderBy(e => e.Student!.FullName)
                .Select(e => new
                {
                    e.Id, e.Student!.FullName, e.Mode,
                    Attempts = e.QuizAttempts.Count(a => a.QuizId == quizId && a.SubmittedAt != null),
                    Best = e.QuizAttempts.Where(a => a.QuizId == quizId).Max(a => a.ScorePercent),
                    Last = e.QuizAttempts.Where(a => a.QuizId == quizId).Max(a => a.SubmittedAt),
                    Recorded = e.QuizAttempts.Any(a => a.QuizId == quizId && a.Source == AttemptSource.Recorded),
                })
                .ToListAsync(ct))
            .Where(e => quiz.Audience.Includes(e.Mode))
            .Select(e => new QuizResultRow(e.Id, e.FullName, e.Mode, e.Attempts, e.Best, e.Last, e.Recorded))
            .ToList();

        return new QuizResultsModel(course.Value, quiz.Id, quiz.Title, quiz.Kind, quiz.Audience, quiz.PassingScore, rows);
    }

    public async Task<Result> RecordScoreAsync(int courseId, int quizId, RecordScoreInput input, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var valid = await db.Quizzes.AnyAsync(q => q.Id == quizId && q.CourseId == courseId, ct)
                    && await db.Enrollments.AnyAsync(e => e.Id == input.EnrollmentId && e.CourseId == courseId, ct);
        if (!valid)
        {
            return Error.NotFound();
        }

        var now = time.UtcNow;
        var attempt = await db.QuizAttempts.FirstOrDefaultAsync(a => a.QuizId == quizId && a.EnrollmentId == input.EnrollmentId
                                                                     && a.Source == AttemptSource.Recorded, ct);
        if (attempt is null)
        {
            attempt = new QuizAttempt { QuizId = quizId, EnrollmentId = input.EnrollmentId, Source = AttemptSource.Recorded, StartedAt = now };
            db.QuizAttempts.Add(attempt);
        }

        attempt.SubmittedAt = now;
        attempt.ScorePercent = Math.Round(input.Score, 2);
        attempt.EarnedPoints = attempt.ScorePercent.Value;
        attempt.TotalPoints = 100;
        attempt.RecordedById = user.UserId;
        attempt.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> EditableQuizAsync(int courseId, int quizId, CancellationToken ct)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        if (!await db.Quizzes.AnyAsync(q => q.Id == quizId && q.CourseId == courseId, ct))
        {
            return Error.NotFound();
        }

        return await db.QuizAttempts.AnyAsync(a => a.QuizId == quizId, ct)
            ? Error.Conflict("Students already took this quiz, so its questions are locked to keep results fair.")
            : Result.Success();
    }

    private async Task<Error?> ApplyAsync(Quiz quiz, QuizInput input, CancellationToken ct)
    {
        if (input.LessonId is { } lessonId && !await db.Lessons.AnyAsync(l => l.Id == lessonId && l.CourseId == quiz.CourseId, ct))
        {
            return Error.Validation("The selected lesson does not belong to this course.", nameof(QuizInput.LessonId));
        }

        if (input.Kind == QuizKind.FinalExam
            && await db.Quizzes.AnyAsync(q => q.CourseId == quiz.CourseId && q.Kind == QuizKind.FinalExam && q.Id != quiz.Id
                                              && (q.Audience == QuizAudience.All || input.Audience == QuizAudience.All || q.Audience == input.Audience), ct))
        {
            return Error.Validation("This course already has a final exam for these students.", nameof(QuizInput.Kind));
        }

        quiz.Title = input.Title.Trim();
        quiz.Instructions = string.IsNullOrWhiteSpace(input.Instructions) ? null : input.Instructions.Trim();
        quiz.Kind = input.Kind;
        quiz.Audience = input.Audience;
        quiz.LessonId = input.LessonId;
        quiz.TimeLimitMinutes = input.TimeLimitMinutes;
        quiz.PassingScore = input.PassingScore;
        quiz.MaxAttempts = input.MaxAttempts;
        quiz.AvailableFrom = input.AvailableFrom is { } from ? time.ToUtc(from) : null;
        quiz.AvailableUntil = input.AvailableUntil is { } until ? time.ToUtc(until) : null;
        quiz.IsPublished = input.IsPublished;
        return null;
    }

    private async Task<IReadOnlyList<LessonChoice>> LessonsAsync(int courseId, CancellationToken ct)
        => await db.Lessons.AsNoTracking().Where(l => l.CourseId == courseId).OrderBy(l => l.SortOrder)
            .Select(l => new LessonChoice(l.Id, l.Title)).ToListAsync(ct);
}

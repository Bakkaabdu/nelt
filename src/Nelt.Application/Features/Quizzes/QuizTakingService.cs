using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Application.Features.Learning;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Quizzes;

public interface IQuizTakingService
{
    Task<Result<QuizIntro>> IntroAsync(int courseId, int quizId, CancellationToken ct = default);
    Task<Result<int>> StartAsync(int courseId, int quizId, CancellationToken ct = default);
    Task<Result<AttemptSheet>> SheetAsync(int courseId, int attemptId, CancellationToken ct = default);
    Task<Result> SubmitAsync(int courseId, int attemptId, IReadOnlyDictionary<int, int[]> answers, CancellationToken ct = default);
    Task<Result<AttemptReview>> ReviewAsync(int courseId, int attemptId, CancellationToken ct = default);
}

internal sealed class QuizTakingService(IAppDbContext db, ICourseAccess access, IPlatformTime time) : IQuizTakingService
{
    /// <summary>Network latency allowance after the deadline.</summary>
    private static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(60);

    public async Task<Result<QuizIntro>> IntroAsync(int courseId, int quizId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(courseId, quizId, ct);
        if (ctx.Failed)
        {
            return ctx.Error!;
        }

        var (enrollment, quiz) = ctx.Value;
        await CloseExpiredAttemptsAsync(enrollment.Id, quiz.Id, ct);

        var attempts = await db.QuizAttempts.AsNoTracking()
            .Where(a => a.QuizId == quiz.Id && a.EnrollmentId == enrollment.Id && a.Source == AttemptSource.Online)
            .OrderByDescending(a => a.StartedAt)
            .Select(a => new { a.Id, a.SubmittedAt, a.ScorePercent })
            .ToListAsync(ct);

        var best = await db.QuizAttempts.Where(a => a.QuizId == quiz.Id && a.EnrollmentId == enrollment.Id).MaxAsync(a => a.ScorePercent, ct);
        var questions = await db.Questions.Where(q => q.QuizId == quiz.Id).Select(q => q.Points).ToListAsync(ct);
        var open = attempts.FirstOrDefault(a => a.SubmittedAt is null)?.Id;
        var last = attempts.FirstOrDefault(a => a.SubmittedAt is not null)?.Id;
        var block = open is not null ? QuizBlockReason.None : await BlockReasonAsync(enrollment, quiz, attempts.Count, questions.Count, ct);

        return new QuizIntro(LearningService.Header(enrollment.Course!), quiz.Id, quiz.Title, quiz.Instructions, quiz.Kind, questions.Count,
            questions.Sum(), quiz.TimeLimitMinutes, quiz.PassingScore, attempts.Count, quiz.MaxAttempts, best, open, last, quiz.AvailableUntil, block);
    }

    public async Task<Result<int>> StartAsync(int courseId, int quizId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(courseId, quizId, ct);
        if (ctx.Failed)
        {
            return ctx.Error!;
        }

        var (enrollment, quiz) = ctx.Value;
        await CloseExpiredAttemptsAsync(enrollment.Id, quiz.Id, ct);

        var open = await db.QuizAttempts.Where(a => a.QuizId == quiz.Id && a.EnrollmentId == enrollment.Id && a.SubmittedAt == null)
            .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (open is { } openId)
        {
            return openId;
        }

        var used = await db.QuizAttempts.CountAsync(a => a.QuizId == quiz.Id && a.EnrollmentId == enrollment.Id && a.Source == AttemptSource.Online, ct);
        var questionCount = await db.Questions.CountAsync(q => q.QuizId == quiz.Id, ct);
        var block = await BlockReasonAsync(enrollment, quiz, used, questionCount, ct);
        if (block != QuizBlockReason.None)
        {
            return Error.Conflict(BlockMessage(block));
        }

        var now = time.UtcNow;
        DateTime? deadline = quiz.TimeLimitMinutes is { } minutes ? now.AddMinutes(minutes) : null;
        if (quiz.AvailableUntil is { } closes && (deadline is null || closes < deadline))
        {
            deadline = closes;
        }

        var attempt = new QuizAttempt { QuizId = quiz.Id, EnrollmentId = enrollment.Id, Source = AttemptSource.Online, StartedAt = now, DeadlineAt = deadline };
        db.QuizAttempts.Add(attempt);
        enrollment.LastActivityAt = now;
        await db.SaveChangesAsync(ct);
        return attempt.Id;
    }

    public async Task<Result<AttemptSheet>> SheetAsync(int courseId, int attemptId, CancellationToken ct = default)
    {
        var attemptResult = await OwnAttemptAsync(courseId, attemptId, ct);
        if (attemptResult.Failed)
        {
            return attemptResult.Error!;
        }

        var (enrollment, attempt) = attemptResult.Value;
        if (attempt.IsSubmitted)
        {
            return Error.Conflict("This attempt was already submitted.");
        }

        if (IsExpired(attempt))
        {
            await GradeAsync(attempt, new Dictionary<int, int[]>(), ct);
            return Error.Conflict("Time is up. Your attempt was closed.");
        }

        var quiz = attempt.Quiz!;
        var questions = await db.Questions.AsNoTracking().Where(q => q.QuizId == quiz.Id)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .Select(q => new { q.Id, q.Type, q.Prompt, q.Points, Options = q.Options.OrderBy(o => o.SortOrder).Select(o => new TakeOption(o.Id, o.Text)).ToList() })
            .ToListAsync(ct);

        var sheet = questions.Select((q, i) => new TakeQuestion(q.Id, i + 1, q.Type, q.Prompt, q.Points, q.Options)).ToList();
        return new AttemptSheet(LearningService.Header(enrollment.Course!), attempt.Id, quiz.Id, quiz.Title, quiz.Kind, attempt.DeadlineAt, sheet);
    }

    public async Task<Result> SubmitAsync(int courseId, int attemptId, IReadOnlyDictionary<int, int[]> answers, CancellationToken ct = default)
    {
        var attemptResult = await OwnAttemptAsync(courseId, attemptId, ct);
        if (attemptResult.Failed)
        {
            return attemptResult;
        }

        var (_, attempt) = attemptResult.Value;
        if (attempt.IsSubmitted)
        {
            return Result.Success(); // Idempotent: a double click must not fail.
        }

        // Answers that arrive after the deadline (plus grace) are discarded: the attempt is closed with what was on time (nothing).
        await GradeAsync(attempt, IsExpired(attempt) ? new Dictionary<int, int[]>() : answers, ct);
        return Result.Success();
    }

    public async Task<Result<AttemptReview>> ReviewAsync(int courseId, int attemptId, CancellationToken ct = default)
    {
        var attemptResult = await OwnAttemptAsync(courseId, attemptId, ct);
        if (attemptResult.Failed)
        {
            return attemptResult.Error!;
        }

        var (enrollment, attempt) = attemptResult.Value;
        if (!attempt.IsSubmitted)
        {
            return Error.Conflict("This attempt is still in progress.");
        }

        var quiz = attempt.Quiz!;
        var used = await db.QuizAttempts.CountAsync(a => a.QuizId == quiz.Id && a.EnrollmentId == enrollment.Id && a.Source == AttemptSource.Online, ct);
        var canRetry = used < quiz.MaxAttempts && quiz.IsOpenAt(time.UtcNow);
        var reveal = quiz.Kind == QuizKind.Quiz && !canRetry;

        var answers = await db.AttemptAnswers.AsNoTracking().Where(a => a.AttemptId == attempt.Id).ToListAsync(ct);
        var questions = await db.Questions.AsNoTracking().Where(q => q.QuizId == quiz.Id)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .Include(q => q.Options)
            .ToListAsync(ct);

        var review = questions.Select((q, i) =>
        {
            var answer = answers.FirstOrDefault(a => a.QuestionId == q.Id);
            var selected = ParseIds(answer?.SelectedOptionIds);
            var options = q.Options.OrderBy(o => o.SortOrder)
                .Select(o => new ReviewOption(o.Id, o.Text, selected.Contains(o.Id), reveal ? o.IsCorrect : null))
                .ToList();
            return new ReviewQuestion(i + 1, q.Prompt, q.Points, answer?.PointsAwarded ?? 0, options);
        }).ToList();

        return new AttemptReview(LearningService.Header(enrollment.Course!), quiz.Id, quiz.Title, quiz.Kind, attempt.ScorePercent ?? 0,
            attempt.EarnedPoints, attempt.TotalPoints, quiz.PassingScore, attempt.SubmittedAt!.Value, reveal, canRetry, review);
    }

    private async Task GradeAsync(QuizAttempt attempt, IReadOnlyDictionary<int, int[]> answers, CancellationToken ct)
    {
        var questions = await db.Questions.AsNoTracking().Where(q => q.QuizId == attempt.QuizId).Include(q => q.Options).ToListAsync(ct);

        decimal earned = 0, total = 0;
        foreach (var question in questions)
        {
            total += question.Points;
            var validIds = question.Options.Select(o => o.Id).ToHashSet();
            var selected = answers.TryGetValue(question.Id, out var ids) ? ids.Where(validIds.Contains).Distinct().ToArray() : [];
            var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToArray();
            var points = QuizGrader.Score(question.Type, question.Points, correct, selected);
            earned += points;

            if (selected.Length > 0)
            {
                attempt.Answers.Add(new AttemptAnswer { QuestionId = question.Id, SelectedOptionIds = string.Join(',', selected), PointsAwarded = points });
            }
        }

        attempt.EarnedPoints = earned;
        attempt.TotalPoints = total;
        attempt.ScorePercent = QuizGrader.Percent(earned, total);
        attempt.SubmittedAt = time.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task CloseExpiredAttemptsAsync(int enrollmentId, int quizId, CancellationToken ct)
    {
        var limit = time.UtcNow - SubmitGrace;
        var expired = await db.QuizAttempts
            .Where(a => a.QuizId == quizId && a.EnrollmentId == enrollmentId && a.SubmittedAt == null && a.DeadlineAt != null && a.DeadlineAt < limit)
            .ToListAsync(ct);

        foreach (var attempt in expired)
        {
            await GradeAsync(attempt, new Dictionary<int, int[]>(), ct);
        }
    }

    private async Task<QuizBlockReason> BlockReasonAsync(Enrollment enrollment, Quiz quiz, int attemptsUsed, int questionCount, CancellationToken ct)
    {
        if (questionCount == 0)
        {
            return QuizBlockReason.NoQuestions;
        }

        if (!quiz.IsOpenAt(time.UtcNow))
        {
            return QuizBlockReason.NotOpen;
        }

        if (attemptsUsed >= quiz.MaxAttempts)
        {
            return QuizBlockReason.NoAttemptsLeft;
        }

        if (quiz.Kind == QuizKind.FinalExam && enrollment.Mode == StudyMode.Online && enrollment.Course!.Policy.RequireAllLessons)
        {
            var total = await db.Lessons.CountAsync(l => l.CourseId == quiz.CourseId && l.IsPublished, ct);
            var done = await db.LessonProgress.CountAsync(p => p.EnrollmentId == enrollment.Id && p.Lesson!.IsPublished, ct);
            if (done < total)
            {
                return QuizBlockReason.LessonsIncomplete;
            }
        }

        return QuizBlockReason.None;
    }

    private static string BlockMessage(QuizBlockReason reason) => reason switch
    {
        QuizBlockReason.NotOpen => "This quiz is not open right now.",
        QuizBlockReason.NoAttemptsLeft => "You have used all attempts for this quiz.",
        QuizBlockReason.LessonsIncomplete => "Finish all lessons to unlock the final exam.",
        QuizBlockReason.NotForYourMode => "This quiz is not intended for your study mode.",
        _ => "This quiz has no questions yet.",
    };

    private async Task<Result<(Enrollment Enrollment, Quiz Quiz)>> LoadAsync(int courseId, int quizId, CancellationToken ct)
    {
        var enrollment = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollment.Failed)
        {
            return enrollment.Error!;
        }

        var quiz = await db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quizId && q.CourseId == courseId && q.IsPublished, ct);
        if (quiz is null)
        {
            return Error.NotFound();
        }

        return quiz.Audience.Includes(enrollment.Value.Mode)
            ? (enrollment.Value, quiz)
            : Error.Forbidden(BlockMessage(QuizBlockReason.NotForYourMode));
    }

    private async Task<Result<(Enrollment Enrollment, QuizAttempt Attempt)>> OwnAttemptAsync(int courseId, int attemptId, CancellationToken ct)
    {
        var enrollment = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollment.Failed)
        {
            return enrollment.Error!;
        }

        var attempt = await db.QuizAttempts.Include(a => a.Quiz)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.EnrollmentId == enrollment.Value.Id && a.Source == AttemptSource.Online, ct);

        return attempt is null ? Error.NotFound() : (enrollment.Value, attempt);
    }

    private bool IsExpired(QuizAttempt attempt) => attempt.DeadlineAt is { } deadline && time.UtcNow > deadline + SubmitGrace;

    private static HashSet<int> ParseIds(string? csv)
        => string.IsNullOrEmpty(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var id) ? id : 0).Where(id => id > 0).ToHashSet();
}

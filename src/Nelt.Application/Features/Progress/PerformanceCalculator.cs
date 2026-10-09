using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Progress;

/// <summary>
/// Builds <see cref="PerformanceSnapshot"/>s for one or all enrollments of a course using a fixed number of
/// set-based queries (no N+1), so rosters of any size stay cheap.
/// </summary>
internal sealed class PerformanceCalculator(IAppDbContext db)
{
    public async Task<PerformanceSnapshot> ForEnrollmentAsync(int courseId, int enrollmentId, CancellationToken ct)
    {
        var all = await ForCourseAsync(courseId, enrollmentId, ct);
        return all.TryGetValue(enrollmentId, out var snapshot) ? snapshot : new PerformanceSnapshot();
    }

    public async Task<Dictionary<int, PerformanceSnapshot>> ForCourseAsync(int courseId, int? onlyEnrollmentId, CancellationToken ct)
    {
        var enrollmentsQuery = db.Enrollments.AsNoTracking().Where(e => e.CourseId == courseId);
        if (onlyEnrollmentId is { } only)
        {
            enrollmentsQuery = enrollmentsQuery.Where(e => e.Id == only);
        }

        var enrollments = await enrollmentsQuery.Select(e => new { e.Id, e.Mode }).ToListAsync(ct);
        if (enrollments.Count == 0)
        {
            return [];
        }

        bool Included(int enrollmentId) => onlyEnrollmentId is null || enrollmentId == onlyEnrollmentId;

        var lessonsTotal = await db.Lessons.CountAsync(l => l.CourseId == courseId && l.IsPublished, ct);

        var lessonsDone = (await db.LessonProgress.AsNoTracking()
                .Where(p => p.Enrollment!.CourseId == courseId && p.Lesson!.IsPublished)
                .Where(p => onlyEnrollmentId == null || p.EnrollmentId == onlyEnrollmentId)
                .GroupBy(p => p.EnrollmentId)
                .Select(g => new { EnrollmentId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.EnrollmentId, x => x.Count);

        var quizzes = await db.Quizzes.AsNoTracking()
            .Where(q => q.CourseId == courseId && q.IsPublished)
            .Select(q => new { q.Id, q.Kind, q.Audience })
            .ToListAsync(ct);

        var bestScores = (await db.QuizAttempts.AsNoTracking()
                .Where(a => a.Enrollment!.CourseId == courseId && a.ScorePercent != null)
                .Where(a => onlyEnrollmentId == null || a.EnrollmentId == onlyEnrollmentId)
                .GroupBy(a => new { a.EnrollmentId, a.QuizId })
                .Select(g => new { g.Key.EnrollmentId, g.Key.QuizId, Best = g.Max(a => a.ScorePercent) })
                .ToListAsync(ct))
            .ToDictionary(x => (x.EnrollmentId, x.QuizId), x => x.Best ?? 0m);

        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.CourseId == courseId && a.IsPublished)
            .Select(a => new { a.Id, a.MaxScore })
            .ToListAsync(ct);

        var submissions = (await db.Submissions.AsNoTracking()
                .Where(s => s.Enrollment!.CourseId == courseId && s.Assignment!.IsPublished)
                .Where(s => onlyEnrollmentId == null || s.EnrollmentId == onlyEnrollmentId)
                .Select(s => new { s.EnrollmentId, s.AssignmentId, s.Score })
                .ToListAsync(ct))
            .ToDictionary(s => (s.EnrollmentId, s.AssignmentId), s => s.Score);

        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.Enrollment!.CourseId == courseId)
            .Where(r => onlyEnrollmentId == null || r.EnrollmentId == onlyEnrollmentId)
            .GroupBy(r => new { r.EnrollmentId, r.Status })
            .Select(g => new { g.Key.EnrollmentId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var result = new Dictionary<int, PerformanceSnapshot>(enrollments.Count);
        foreach (var e in enrollments.Where(e => Included(e.Id)))
        {
            var applicable = quizzes.Where(q => q.Audience.Includes(e.Mode)).ToList();
            var regular = applicable.Where(q => q.Kind == QuizKind.Quiz).ToList();
            var finals = applicable.Where(q => q.Kind == QuizKind.FinalExam).ToList();

            var regularScores = regular.Select(q => bestScores.TryGetValue((e.Id, q.Id), out var s) ? (decimal?)s : null).ToList();
            var finalScores = finals.Select(q => bestScores.TryGetValue((e.Id, q.Id), out var s) ? (decimal?)s : null)
                .Where(s => s is not null).ToList();

            int submitted = 0, awaiting = 0, counted = 0;
            decimal assignmentSum = 0;
            foreach (var a in assignments)
            {
                if (!submissions.TryGetValue((e.Id, a.Id), out var score))
                {
                    counted++;
                    continue;
                }

                submitted++;
                if (score is null)
                {
                    awaiting++;
                    continue;
                }

                counted++;
                assignmentSum += a.MaxScore <= 0 ? 0 : Math.Clamp(score.Value / a.MaxScore * 100m, 0, 100);
            }

            var records = attendance.Where(r => r.EnrollmentId == e.Id).ToList();
            var attended = records.Where(r => r.Status.CountsAsAttended()).Sum(r => r.Count);
            var absent = records.Where(r => r.Status == AttendanceStatus.Absent).Sum(r => r.Count);

            result[e.Id] = new PerformanceSnapshot
            {
                Mode = e.Mode,
                LessonsTotal = lessonsTotal,
                LessonsCompleted = Math.Min(lessonsTotal, lessonsDone.GetValueOrDefault(e.Id)),
                QuizzesTotal = regular.Count,
                QuizzesTaken = regularScores.Count(s => s is not null),
                QuizAverage = regular.Count == 0 ? null : Math.Round(regularScores.Sum(s => s ?? 0m) / regular.Count, 1),
                AssignmentsTotal = assignments.Count,
                AssignmentsSubmitted = submitted,
                AssignmentsAwaitingGrade = awaiting,
                AssignmentAverage = counted == 0 ? null : Math.Round(assignmentSum / counted, 1),
                HasFinalExam = finals.Count > 0,
                FinalExamScore = finalScores.Count == 0 ? null : finalScores.Max(),
                SessionsAttended = attended,
                SessionsCounted = attended + absent,
            };
        }

        return result;
    }
}

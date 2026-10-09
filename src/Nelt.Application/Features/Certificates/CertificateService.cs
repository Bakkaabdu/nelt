using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Application.Features.Catalog;
using Nelt.Application.Features.Learning;
using Nelt.Application.Features.Progress;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Domain.Services;

namespace Nelt.Application.Features.Certificates;

public sealed record CertificateOverview(
    CourseHeader Course,
    StudyMode Mode,
    EnrollmentStatus EnrollmentStatus,
    CompletionPolicy Policy,
    PerformanceSnapshot Performance,
    CompletionEvaluation Evaluation,
    CertificateStatus? RequestStatus,
    string? ReviewNote,
    string? SerialNumber,
    IReadOnlyList<CourseCard> NextLevelCourses);

public sealed record CertificateDocument(
    string SerialNumber,
    string StudentName,
    LocalizedText CourseTitle,
    string LevelCode,
    LocalizedText LevelName,
    TargetLanguage Language,
    decimal FinalScore,
    bool EligibleForNextLevel,
    DateTime IssuedAt,
    int? TotalHours,
    string? InstructorName);

public sealed record CertificateRequestRow(
    int Id,
    int CourseId,
    int EnrollmentId,
    string StudentName,
    LocalizedText CourseTitle,
    string LevelCode,
    DateTime RequestedAt,
    decimal FinalScore,
    bool EligibleForNextLevel,
    CertificateStatus Status,
    string? SerialNumber,
    DateTime? ReviewedAt);

public sealed class ReviewInput
{
    [StringLength(500), Display(Name = "Note to the student")]
    public string? Note { get; set; }
}

public interface ICertificateService
{
    Task<Result<CertificateOverview>> OverviewAsync(int courseId, CancellationToken ct = default);
    Task<Result> RequestAsync(int courseId, CancellationToken ct = default);
    Task<Result<CertificateDocument>> MyCertificateAsync(int courseId, CancellationToken ct = default);
    Task<IReadOnlyList<CertificateRequestRow>> RequestsAsync(CertificateStatus? status, CancellationToken ct = default);
    Task<Result> ApproveAsync(int requestId, CancellationToken ct = default);
    Task<Result> RejectAsync(int requestId, ReviewInput input, CancellationToken ct = default);
    Task<CertificateDocument?> VerifyAsync(string serialNumber, CancellationToken ct = default);
}

internal sealed class CertificateService(
    IAppDbContext db,
    ICourseAccess access,
    ICurrentUser user,
    IPlatformTime time,
    PerformanceCalculator performance,
    ContentCache cache) : ICertificateService
{
    public async Task<Result<CertificateOverview>> OverviewAsync(int courseId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult.Error!;
        }

        var enrollment = enrollmentResult.Value;
        var course = enrollment.Course!;
        var snapshot = await performance.ForEnrollmentAsync(courseId, enrollment.Id, ct);
        var evaluation = CompletionEvaluator.Evaluate(course.Policy, snapshot);
        var request = await db.CertificateRequests.AsNoTracking().FirstOrDefaultAsync(r => r.EnrollmentId == enrollment.Id, ct);

        IReadOnlyList<CourseCard> next = [];
        if (evaluation.IsNextLevelEligible || request?.EligibleForNextLevel == true)
        {
            var level = course.Level!;
            next = await db.Courses.AsNoTracking()
                .Where(c => c.IsPublished && c.Level!.Language == level.Language && c.Level.Rank == level.Rank + 1)
                .OrderBy(c => c.StartDate)
                .Take(3)
                .Select(c => new CourseCard(c.Id, c.Slug, c.Title, c.Summary, c.ScheduleNote, c.Level!.Language, c.Level.Code, c.Level.Name,
                    c.DeliveryMode, c.Price, c.StartDate, c.EndDate, c.TotalHours, c.CoverImageKey, null))
                .ToListAsync(ct);
        }

        return new CertificateOverview(LearningService.Header(course), enrollment.Mode, enrollment.Status, course.Policy, snapshot, evaluation,
            request?.Status, request?.ReviewNote, request?.SerialNumber, next);
    }

    public async Task<Result> RequestAsync(int courseId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult;
        }

        var enrollment = enrollmentResult.Value;
        var snapshot = await performance.ForEnrollmentAsync(courseId, enrollment.Id, ct);
        var evaluation = CompletionEvaluator.Evaluate(enrollment.Course!.Policy, snapshot);
        if (!evaluation.IsCertificateEligible)
        {
            return Error.Conflict("You do not meet the completion requirements yet.");
        }

        var request = await db.CertificateRequests.FirstOrDefaultAsync(r => r.EnrollmentId == enrollment.Id, ct);
        if (request is { Status: CertificateStatus.Pending or CertificateStatus.Approved })
        {
            return Result.Success();
        }

        if (request is null)
        {
            request = new CertificateRequest { EnrollmentId = enrollment.Id };
            db.CertificateRequests.Add(request);
        }

        request.Status = CertificateStatus.Pending;
        request.RequestedAt = time.UtcNow;
        request.FinalScore = evaluation.OverallScore;
        request.EligibleForNextLevel = evaluation.IsNextLevelEligible;
        request.ReviewNote = null;
        request.ReviewedAt = null;
        request.ReviewedById = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (db.IsUniqueViolation(ex))
        {
            db.ClearChangeTracker(); // Double submit: the first request is already recorded.
        }

        return Result.Success();
    }

    public async Task<Result<CertificateDocument>> MyCertificateAsync(int courseId, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult.Error!;
        }

        var enrollmentId = enrollmentResult.Value.Id;
        var document = await Documents(db.CertificateRequests.Where(r => r.EnrollmentId == enrollmentId && r.Status == CertificateStatus.Approved))
            .FirstOrDefaultAsync(ct);
        return document is null ? Error.NotFound("No certificate has been issued yet.") : document;
    }

    public async Task<IReadOnlyList<CertificateRequestRow>> RequestsAsync(CertificateStatus? status, CancellationToken ct = default)
    {
        var courses = access.ManageableCourses().Select(c => c.Id);
        var query = db.CertificateRequests.AsNoTracking().Where(r => courses.Contains(r.Enrollment!.CourseId));
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        return await query
            .OrderBy(r => r.Status).ThenByDescending(r => r.RequestedAt)
            .Take(300)
            .Select(r => new CertificateRequestRow(r.Id, r.Enrollment!.CourseId, r.EnrollmentId, r.Enrollment.Student!.FullName, r.Enrollment.Course!.Title,
                r.Enrollment.Course.Level!.Code, r.RequestedAt, r.FinalScore, r.EligibleForNextLevel, r.Status, r.SerialNumber, r.ReviewedAt))
            .ToListAsync(ct);
    }

    public async Task<Result> ApproveAsync(int requestId, CancellationToken ct = default)
    {
        var request = await db.CertificateRequests.Include(r => r.Enrollment).ThenInclude(e => e!.Course)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            return Error.NotFound();
        }

        var enrollment = request.Enrollment!;
        if (!await access.CanManageAsync(enrollment.CourseId, ct))
        {
            return Error.Forbidden();
        }

        if (request.Status == CertificateStatus.Approved)
        {
            return Result.Success();
        }

        // Re-evaluate at approval time: grades may have changed since the request.
        var snapshot = await performance.ForEnrollmentAsync(enrollment.CourseId, enrollment.Id, ct);
        var evaluation = CompletionEvaluator.Evaluate(enrollment.Course!.Policy, snapshot);
        if (!evaluation.IsCertificateEligible)
        {
            return Error.Conflict("The student no longer meets the completion requirements.");
        }

        var now = time.UtcNow;
        request.Status = CertificateStatus.Approved;
        request.FinalScore = evaluation.OverallScore;
        request.EligibleForNextLevel = evaluation.IsNextLevelEligible;
        request.ReviewedAt = now;
        request.ReviewedById = user.UserId;
        request.IssuedAt = now;
        request.SerialNumber = CertificateSerial.New(time.LocalNow);
        enrollment.Complete(now);

        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    public async Task<Result> RejectAsync(int requestId, ReviewInput input, CancellationToken ct = default)
    {
        var request = await db.CertificateRequests.Include(r => r.Enrollment).FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!await access.CanManageAsync(request.Enrollment!.CourseId, ct))
        {
            return Error.Forbidden();
        }

        if (request.Status == CertificateStatus.Approved)
        {
            return Error.Conflict("An issued certificate cannot be declined.");
        }

        request.Status = CertificateStatus.Rejected;
        request.ReviewNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        request.ReviewedAt = time.UtcNow;
        request.ReviewedById = user.UserId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<CertificateDocument?> VerifyAsync(string serialNumber, CancellationToken ct = default)
    {
        var serial = serialNumber.Trim().ToUpperInvariant();
        if (serial.Length is < 8 or > 40)
        {
            return null;
        }

        return await Documents(db.CertificateRequests.Where(r => r.SerialNumber == serial && r.Status == CertificateStatus.Approved))
            .FirstOrDefaultAsync(ct);
    }

    private static IQueryable<CertificateDocument> Documents(IQueryable<CertificateRequest> query)
        => query.AsNoTracking().Select(r => new CertificateDocument(
            r.SerialNumber!,
            r.Enrollment!.Student!.FullName,
            r.Enrollment.Course!.Title,
            r.Enrollment.Course.Level!.Code,
            r.Enrollment.Course.Level.Name,
            r.Enrollment.Course.Level.Language,
            r.FinalScore,
            r.EligibleForNextLevel,
            r.IssuedAt!.Value,
            r.Enrollment.Course.TotalHours,
            r.Enrollment.Course.Instructor != null ? r.Enrollment.Course.Instructor.FullName : null));
}

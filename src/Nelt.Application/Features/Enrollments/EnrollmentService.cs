using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Enrollments;

public sealed record EnrollmentRow(
    int Id,
    Guid StudentId,
    string StudentName,
    string StudentEmail,
    string? StudentPhone,
    int CourseId,
    LocalizedText CourseTitle,
    string LevelCode,
    decimal CoursePrice,
    StudyMode Mode,
    EnrollmentStatus Status,
    DateTime CreatedAt,
    DateTime? ActivatedAt,
    decimal? AmountPaid,
    string? PaymentReference);

public sealed record EnrollmentFilter(EnrollmentStatus? Status = null, int? CourseId = null, string? Search = null, int Page = 1);

public sealed class ConfirmPaymentInput
{
    [Range(typeof(decimal), "0", "1000000"), Display(Name = "Amount paid")]
    public decimal? AmountPaid { get; set; }

    [StringLength(120), Display(Name = "Receipt / reference")]
    public string? PaymentReference { get; set; }
}

public sealed class ManualEnrollmentInput
{
    [Required, Display(Name = "Student")]
    public Guid? StudentId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please choose a course."), Display(Name = "Course")]
    public int CourseId { get; set; }

    [Display(Name = "Study mode")]
    public StudyMode Mode { get; set; } = StudyMode.InPerson;

    [Display(Name = "Payment received")]
    public bool Activate { get; set; } = true;

    [Range(typeof(decimal), "0", "1000000"), Display(Name = "Amount paid")]
    public decimal? AmountPaid { get; set; }

    [StringLength(120), Display(Name = "Receipt / reference")]
    public string? PaymentReference { get; set; }
}

public interface IEnrollmentService
{
    /// <summary>A signed-in student reserves a seat. The enrollment stays pending until payment is confirmed.</summary>
    Task<Result<int>> RequestAsync(int courseId, StudyMode mode, CancellationToken ct = default);

    Task<PagedList<EnrollmentRow>> ListAsync(EnrollmentFilter filter, CancellationToken ct = default);
    Task<Result> ConfirmAsync(int enrollmentId, ConfirmPaymentInput input, CancellationToken ct = default);
    Task<Result> CancelAsync(int enrollmentId, CancellationToken ct = default);
    Task<Result> ChangeModeAsync(int enrollmentId, StudyMode mode, CancellationToken ct = default);
    Task<Result<int>> EnrollManuallyAsync(ManualEnrollmentInput input, CancellationToken ct = default);
}

internal sealed class EnrollmentService(IAppDbContext db, ICurrentUser user, IPlatformTime time, ContentCache cache) : IEnrollmentService
{
    public async Task<Result<int>> RequestAsync(int courseId, StudyMode mode, CancellationToken ct = default)
    {
        var studentId = user.RequiredUserId;
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId && c.IsPublished, ct);
        if (course is null)
        {
            return Error.NotFound();
        }

        var existing = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == courseId && e.StudentId == studentId, ct);
        if (existing is not null && existing.Status != EnrollmentStatus.Cancelled)
        {
            return existing.Id;
        }

        return await CreateOrReopenAsync(course, studentId, mode, existing, activate: false, null, null, ct);
    }

    public Task<PagedList<EnrollmentRow>> ListAsync(EnrollmentFilter filter, CancellationToken ct = default)
    {
        var query = db.Enrollments.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(e => e.Status == status);
        }

        if (filter.CourseId is { } courseId)
        {
            query = query.Where(e => e.CourseId == courseId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e => e.Student!.FullName.Contains(term) || e.Student.Email!.Contains(term) || e.Student.PhoneNumber!.Contains(term));
        }

        return query
            .OrderBy(e => e.Status == EnrollmentStatus.Pending ? 0 : 1).ThenByDescending(e => e.CreatedAt)
            .Select(e => new EnrollmentRow(
                e.Id, e.StudentId, e.Student!.FullName, e.Student.Email ?? string.Empty, e.Student.PhoneNumber,
                e.CourseId, e.Course!.Title, e.Course.Level!.Code, e.Course.Price,
                e.Mode, e.Status, e.CreatedAt, e.ActivatedAt, e.AmountPaid, e.PaymentReference))
            .ToPagedListAsync(filter.Page, ct: ct);
    }

    public async Task<Result> ConfirmAsync(int enrollmentId, ConfirmPaymentInput input, CancellationToken ct = default)
    {
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);
        if (enrollment is null)
        {
            return Error.NotFound();
        }

        if (enrollment.Status is EnrollmentStatus.Completed)
        {
            return Error.Conflict("This enrollment is already completed.");
        }

        enrollment.Activate(time.UtcNow, input.AmountPaid, Clean(input.PaymentReference));
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    public async Task<Result> CancelAsync(int enrollmentId, CancellationToken ct = default)
    {
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);
        if (enrollment is null)
        {
            return Error.NotFound();
        }

        if (enrollment.Status is EnrollmentStatus.Completed)
        {
            return Error.Conflict("This enrollment is already completed.");
        }

        enrollment.Cancel();
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return Result.Success();
    }

    public async Task<Result> ChangeModeAsync(int enrollmentId, StudyMode mode, CancellationToken ct = default)
    {
        var enrollment = await db.Enrollments.Include(e => e.Course).FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);
        if (enrollment is null)
        {
            return Error.NotFound();
        }

        if (!enrollment.Course!.DeliveryMode.Allows(mode))
        {
            return Error.Validation("This course is not offered in the selected mode.");
        }

        enrollment.Mode = mode;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<int>> EnrollManuallyAsync(ManualEnrollmentInput input, CancellationToken ct = default)
    {
        var studentId = input.StudentId!.Value;
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CourseId, ct);
        if (course is null)
        {
            return Error.Validation("Please choose a course.", nameof(ManualEnrollmentInput.CourseId));
        }

        if (!await db.Users.AnyAsync(u => u.Id == studentId, ct))
        {
            return Error.Validation("The selected student was not found.", nameof(ManualEnrollmentInput.StudentId));
        }

        var existing = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == input.CourseId && e.StudentId == studentId, ct);
        if (existing is not null && existing.Status != EnrollmentStatus.Cancelled)
        {
            return Error.Conflict("This student is already enrolled in the course.");
        }

        return await CreateOrReopenAsync(course, studentId, input.Mode, existing, input.Activate, input.AmountPaid, Clean(input.PaymentReference), ct);
    }

    private async Task<Result<int>> CreateOrReopenAsync(
        Course course, Guid studentId, StudyMode mode, Enrollment? existing, bool activate, decimal? amount, string? reference, CancellationToken ct)
    {
        if (!course.DeliveryMode.Allows(mode))
        {
            return Error.Validation("This course is not offered in the selected mode.");
        }

        if (course.Capacity is { } capacity)
        {
            var taken = await db.Enrollments.CountAsync(e => e.CourseId == course.Id
                && (e.Status == EnrollmentStatus.Pending || e.Status == EnrollmentStatus.Active), ct);
            if (taken >= capacity)
            {
                return Error.Conflict("This course is fully booked.");
            }
        }

        var enrollment = existing ?? new Enrollment { CourseId = course.Id, StudentId = studentId };
        enrollment.Mode = mode;
        enrollment.Status = EnrollmentStatus.Pending;
        if (activate)
        {
            enrollment.Activate(time.UtcNow, amount, reference);
        }

        if (existing is null)
        {
            db.Enrollments.Add(enrollment);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (db.IsUniqueViolation(ex))
        {
            // A double-submitted form raced us to the insert; the enrollment exists, which is what the user wanted.
            db.ClearChangeTracker();
            var id = await db.Enrollments.Where(e => e.CourseId == course.Id && e.StudentId == studentId).Select(e => e.Id).FirstAsync(ct);
            return id;
        }

        cache.Invalidate();
        return enrollment.Id;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Access;
using Nelt.Application.Features.Learning;
using Nelt.Application.Features.Quizzes;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Assignments;

public interface IAssignmentService
{
    Task<Result<AssignmentList>> ListAsync(int courseId, CancellationToken ct = default);
    Task<Result<AssignmentEditModel>> GetForEditAsync(int courseId, int? assignmentId, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(int courseId, AssignmentInput input, FileUpload? attachment, CancellationToken ct = default);
    Task<Result> UpdateAsync(int courseId, int assignmentId, AssignmentInput input, FileUpload? attachment, bool removeAttachment, CancellationToken ct = default);
    Task<Result> DeleteAsync(int courseId, int assignmentId, CancellationToken ct = default);
    Task<Result<SubmissionsModel>> SubmissionsAsync(int courseId, int assignmentId, CancellationToken ct = default);
    Task<Result> GradeAsync(int courseId, int submissionId, GradeInput input, CancellationToken ct = default);
}

public interface IStudentAssignmentService
{
    Task<Result<StudentAssignmentList>> ListAsync(int courseId, CancellationToken ct = default);
    Task<Result<StudentAssignmentDetail>> GetAsync(int courseId, int assignmentId, CancellationToken ct = default);
    Task<Result> SubmitAsync(int courseId, int assignmentId, SubmitInput input, FileUpload? file, CancellationToken ct = default);
}

internal sealed class AssignmentService(IAppDbContext db, ICourseAccess access, ICurrentUser user, IFileStorage storage, IPlatformTime time)
    : IAssignmentService
{
    public async Task<Result<AssignmentList>> ListAsync(int courseId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var rows = await db.Assignments.AsNoTracking().Where(a => a.CourseId == courseId)
            .OrderBy(a => a.DueAt == null).ThenBy(a => a.DueAt).ThenBy(a => a.Id)
            .Select(a => new AssignmentRow(a.Id, a.Title, a.DueAt, a.MaxScore, a.IsPublished, a.Submissions.Count, a.Submissions.Count(s => s.Score != null)))
            .ToListAsync(ct);

        var active = await db.Enrollments.CountAsync(e => e.CourseId == courseId && e.Status == EnrollmentStatus.Active, ct);
        return new AssignmentList(course.Value, active, rows);
    }

    public async Task<Result<AssignmentEditModel>> GetForEditAsync(int courseId, int? assignmentId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var lessons = await db.Lessons.AsNoTracking().Where(l => l.CourseId == courseId).OrderBy(l => l.SortOrder)
            .Select(l => new LessonChoice(l.Id, l.Title)).ToListAsync(ct);

        if (assignmentId is null)
        {
            return new AssignmentEditModel(course.Value, null, new AssignmentInput(), lessons, null);
        }

        var a = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId && x.CourseId == courseId, ct);
        if (a is null)
        {
            return Error.NotFound();
        }

        var input = new AssignmentInput
        {
            Title = a.Title,
            Instructions = a.Instructions,
            LessonId = a.LessonId,
            DueAt = a.DueAt is { } due ? time.ToLocal(due) : null,
            MaxScore = a.MaxScore,
            AllowLateSubmissions = a.AllowLateSubmissions,
            IsPublished = a.IsPublished,
        };
        return new AssignmentEditModel(course.Value, a.Id, input, lessons, a.AttachmentName);
    }

    public async Task<Result<int>> CreateAsync(int courseId, AssignmentInput input, FileUpload? attachment, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var assignment = new Assignment { CourseId = courseId };
        var result = await ApplyAsync(assignment, input, attachment, removeAttachment: false, ct);
        if (result.Failed)
        {
            return result.Error!;
        }

        db.Assignments.Add(assignment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteQuietlyAsync(assignment.AttachmentKey);
            throw;
        }

        return assignment.Id;
    }

    public async Task<Result> UpdateAsync(int courseId, int assignmentId, AssignmentInput input, FileUpload? attachment, bool removeAttachment, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.CourseId == courseId, ct);
        if (assignment is null)
        {
            return Error.NotFound();
        }

        var previous = assignment.AttachmentKey;
        var result = await ApplyAsync(assignment, input, attachment, removeAttachment, ct);
        if (result.Failed)
        {
            return result;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (assignment.AttachmentKey != previous)
            {
                await storage.DeleteQuietlyAsync(assignment.AttachmentKey);
            }

            throw;
        }

        if (previous is not null && previous != assignment.AttachmentKey)
        {
            await storage.DeleteQuietlyAsync(previous);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int courseId, int assignmentId, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.CourseId == courseId, ct);
        if (assignment is null)
        {
            return Error.NotFound();
        }

        if (await db.Submissions.AnyAsync(s => s.AssignmentId == assignmentId, ct))
        {
            return Error.Conflict("Students already submitted work for this assignment. Unpublish it instead of deleting it.");
        }

        db.Assignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
        if (assignment.AttachmentKey is not null)
        {
            await storage.DeleteAsync(assignment.AttachmentKey, ct);
        }

        return Result.Success();
    }

    public async Task<Result<SubmissionsModel>> SubmissionsAsync(int courseId, int assignmentId, CancellationToken ct = default)
    {
        var course = await access.ManageAsync(courseId, ct);
        if (course.Failed)
        {
            return course.Error!;
        }

        var assignment = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId && a.CourseId == courseId, ct);
        if (assignment is null)
        {
            return Error.NotFound();
        }

        var rows = await db.Enrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed))
            .Select(e => new
            {
                e.Id, e.Student!.FullName, e.Mode,
                Submission = e.Submissions.Where(s => s.AssignmentId == assignmentId)
                    .Select(s => new { s.Id, s.SubmittedAt, s.IsLate, s.Text, s.FileName, s.Score, s.Feedback })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var model = rows
            .OrderBy(r => r.Submission is null ? 2 : r.Submission.Score is null ? 0 : 1)
            .ThenBy(r => r.FullName)
            .Select(r => new SubmissionRow(r.Id, r.FullName, r.Mode, r.Submission?.Id, r.Submission?.SubmittedAt, r.Submission?.IsLate ?? false,
                r.Submission?.Text, r.Submission?.FileName, r.Submission?.Score, r.Submission?.Feedback))
            .ToList();

        return new SubmissionsModel(course.Value, assignment.Id, assignment.Title, assignment.DueAt, assignment.MaxScore, model);
    }

    public async Task<Result> GradeAsync(int courseId, int submissionId, GradeInput input, CancellationToken ct = default)
    {
        if (!await access.CanManageAsync(courseId, ct))
        {
            return Error.Forbidden();
        }

        var submission = await db.Submissions.Include(s => s.Assignment)
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.Assignment!.CourseId == courseId, ct);
        if (submission is null)
        {
            return Error.NotFound();
        }

        if (input.Score > submission.Assignment!.MaxScore)
        {
            return Error.Validation("The score cannot exceed the maximum score.", nameof(GradeInput.Score));
        }

        submission.Score = input.Score;
        submission.Feedback = string.IsNullOrWhiteSpace(input.Feedback) ? null : input.Feedback.Trim();
        submission.GradedAt = time.UtcNow;
        submission.GradedById = user.UserId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> ApplyAsync(Assignment assignment, AssignmentInput input, FileUpload? attachment, bool removeAttachment, CancellationToken ct)
    {
        if (input.LessonId is { } lessonId && !await db.Lessons.AnyAsync(l => l.Id == lessonId && l.CourseId == assignment.CourseId, ct))
        {
            return Error.Validation("The selected lesson does not belong to this course.", nameof(AssignmentInput.LessonId));
        }

        if (attachment is not null)
        {
            if (FilePolicy.Validate(attachment, FileCategory.Material) is { } error)
            {
                return Result.Fail(error with { Field = "attachment" });
            }

            assignment.AttachmentKey = await storage.SaveAsync(attachment.Content, "assignments", attachment.Extension, ct);
            assignment.AttachmentName = attachment.SafeFileName;
        }
        else if (removeAttachment)
        {
            assignment.AttachmentKey = null;
            assignment.AttachmentName = null;
        }

        assignment.Title = input.Title.Trim();
        assignment.Instructions = input.Instructions.Trim();
        assignment.LessonId = input.LessonId;
        assignment.DueAt = input.DueAt is { } due ? time.ToUtc(due) : null;
        assignment.MaxScore = input.MaxScore;
        assignment.AllowLateSubmissions = input.AllowLateSubmissions;
        assignment.IsPublished = input.IsPublished;
        return Result.Success();
    }
}

internal sealed class StudentAssignmentService(IAppDbContext db, ICourseAccess access, IFileStorage storage, IPlatformTime time)
    : IStudentAssignmentService
{
    public async Task<Result<StudentAssignmentList>> ListAsync(int courseId, CancellationToken ct = default)
    {
        var enrollment = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollment.Failed)
        {
            return enrollment.Error!;
        }

        var enrollmentId = enrollment.Value.Id;
        var rows = await db.Assignments.AsNoTracking()
            .Where(a => a.CourseId == courseId && a.IsPublished)
            .OrderBy(a => a.DueAt == null).ThenBy(a => a.DueAt).ThenBy(a => a.Id)
            .Select(a => new
            {
                a.Id, a.Title, a.DueAt, a.MaxScore, a.AllowLateSubmissions,
                Submission = a.Submissions.Where(s => s.EnrollmentId == enrollmentId).Select(s => new { s.Score }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var now = time.UtcNow;
        var list = rows.Select(r => new StudentAssignmentRow(r.Id, r.Title, r.DueAt, r.MaxScore,
                State(r.Submission is not null, r.Submission?.Score is not null, r.DueAt, r.AllowLateSubmissions, now), r.Submission?.Score))
            .ToList();

        return new StudentAssignmentList(LearningService.Header(enrollment.Value.Course!), list);
    }

    public async Task<Result<StudentAssignmentDetail>> GetAsync(int courseId, int assignmentId, CancellationToken ct = default)
    {
        var enrollment = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollment.Failed)
        {
            return enrollment.Error!;
        }

        var a = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId && x.CourseId == courseId && x.IsPublished, ct);
        if (a is null)
        {
            return Error.NotFound();
        }

        var submission = await db.Submissions.AsNoTracking()
            .Where(s => s.AssignmentId == assignmentId && s.EnrollmentId == enrollment.Value.Id)
            .Select(s => new MySubmission(s.SubmittedAt, s.IsLate, s.Text, s.FileName, s.Score, s.Feedback, s.GradedAt))
            .FirstOrDefaultAsync(ct);

        var now = time.UtcNow;
        var state = State(submission is not null, submission?.Score is not null, a.DueAt, a.AllowLateSubmissions, now);
        var deadlineClosed = a.DueAt is { } due && now > due && !a.AllowLateSubmissions;
        var canSubmit = enrollment.Value.Status == EnrollmentStatus.Active && !deadlineClosed
                        && state is StudentAssignmentState.Open or StudentAssignmentState.Overdue or StudentAssignmentState.Submitted;

        return new StudentAssignmentDetail(LearningService.Header(enrollment.Value.Course!), a.Id, a.Title, a.Instructions, a.DueAt, a.MaxScore,
            a.AttachmentName, state, canSubmit, submission);
    }

    public async Task<Result> SubmitAsync(int courseId, int assignmentId, SubmitInput input, FileUpload? file, CancellationToken ct = default)
    {
        var enrollmentResult = await access.StudentEnrollmentAsync(courseId, ct);
        if (enrollmentResult.Failed)
        {
            return enrollmentResult;
        }

        var enrollment = enrollmentResult.Value;
        if (enrollment.Status != EnrollmentStatus.Active)
        {
            return Error.Forbidden("Your enrollment is not active.");
        }

        var assignment = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId && a.CourseId == courseId && a.IsPublished, ct);
        if (assignment is null)
        {
            return Error.NotFound();
        }

        var now = time.UtcNow;
        var isLate = assignment.DueAt is { } due && now > due;
        if (isLate && !assignment.AllowLateSubmissions)
        {
            return Error.Conflict("The deadline has passed and late submissions are not accepted.");
        }

        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.EnrollmentId == enrollment.Id, ct);
        if (submission?.IsGraded == true)
        {
            return Error.Conflict("Your work was already graded and can no longer be changed.");
        }

        // A resubmission may keep the previously uploaded file and only change (or clear) the text.
        var text = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
        if (text is null && file is null && submission?.FileKey is null)
        {
            return Error.Validation("Write an answer or attach a file.");
        }

        if (file is not null && FilePolicy.Validate(file, FileCategory.Submission) is { } fileError)
        {
            return Result.Fail(fileError with { Field = "file" });
        }

        string? oldFile = null;
        string? newFile = null;
        if (submission is null)
        {
            submission = new Submission { AssignmentId = assignmentId, EnrollmentId = enrollment.Id };
            db.Submissions.Add(submission);
        }

        if (file is not null)
        {
            oldFile = submission.FileKey;
            newFile = await storage.SaveAsync(file.Content, "submissions", file.Extension, ct);
            submission.FileKey = newFile;
            submission.FileName = file.SafeFileName;
            submission.FileSize = file.Length;
        }

        submission.Text = text;
        submission.SubmittedAt = now;
        submission.IsLate = isLate;
        enrollment.LastActivityAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (db.IsUniqueViolation(ex))
        {
            db.ClearChangeTracker();
            await storage.DeleteQuietlyAsync(newFile);
            return Error.Conflict("Your work was already submitted. Refresh the page to see it.");
        }
        catch
        {
            await storage.DeleteQuietlyAsync(newFile);
            throw;
        }

        if (oldFile is not null)
        {
            await storage.DeleteAsync(oldFile, ct);
        }

        return Result.Success();
    }

    private static StudentAssignmentState State(bool submitted, bool graded, DateTime? dueAt, bool allowLate, DateTime now)
    {
        if (graded)
        {
            return StudentAssignmentState.Graded;
        }

        if (submitted)
        {
            return StudentAssignmentState.Submitted;
        }

        if (dueAt is { } due && now > due)
        {
            return allowLate ? StudentAssignmentState.Overdue : StudentAssignmentState.Closed;
        }

        return StudentAssignmentState.Open;
    }
}

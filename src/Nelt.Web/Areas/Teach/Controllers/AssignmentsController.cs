using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Assignments;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Teach.Controllers;

[Route("teach/courses/{courseId:int}/assignments")]
public sealed class AssignmentsController(IAssignmentService assignments) : TeachController
{
    private const long MaxUpload = 210L * 1024 * 1024;

    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await assignments.ListAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpGet("new")]
    public Task<IActionResult> Create(int courseId) => FormAsync(courseId, null, null);

    [HttpPost("new")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<IActionResult> Create(int courseId, [Bind(Prefix = "Input")] AssignmentInput input, IFormFile? attachment)
    {
        if (ModelState.IsValid)
        {
            var result = await assignments.CreateAsync(courseId, input, attachment.ToUpload(), Aborted);
            if (result.Succeeded)
            {
                Flash("The assignment was created.");
                return RedirectToAction(nameof(Index), new { courseId });
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(courseId, null, input);
    }

    [HttpGet("{assignmentId:int}")]
    public Task<IActionResult> Edit(int courseId, int assignmentId) => FormAsync(courseId, assignmentId, null);

    [HttpPost("{assignmentId:int}")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<IActionResult> Edit(int courseId, int assignmentId, [Bind(Prefix = "Input")] AssignmentInput input, IFormFile? attachment, bool removeAttachment)
    {
        if (ModelState.IsValid)
        {
            var result = await assignments.UpdateAsync(courseId, assignmentId, input, attachment.ToUpload(), removeAttachment, Aborted);
            if (result.Succeeded)
            {
                Flash("The assignment was saved.");
                return RedirectToAction(nameof(Index), new { courseId });
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(courseId, assignmentId, input);
    }

    [HttpPost("{assignmentId:int}/delete")]
    public async Task<IActionResult> Delete(int courseId, int assignmentId)
        => RedirectWithResult(await assignments.DeleteAsync(courseId, assignmentId, Aborted), "The assignment was deleted.", nameof(Index), new { courseId });

    [HttpGet("{assignmentId:int}/submissions")]
    public async Task<IActionResult> Submissions(int courseId, int assignmentId)
    {
        var result = await assignments.SubmissionsAsync(courseId, assignmentId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpPost("{assignmentId:int}/submissions/{submissionId:int}")]
    public async Task<IActionResult> Grade(int courseId, int assignmentId, int submissionId, GradeInput input)
    {
        if (!ModelState.IsValid)
        {
            Flash("Please enter a valid score.", FlashKind.Error);
            return RedirectToAction(nameof(Submissions), new { courseId, assignmentId });
        }

        return RedirectWithResult(await assignments.GradeAsync(courseId, submissionId, input, Aborted), "The grade was saved.", nameof(Submissions), new { courseId, assignmentId });
    }

    private async Task<IActionResult> FormAsync(int courseId, int? assignmentId, AssignmentInput? posted)
    {
        var result = await assignments.GetForEditAsync(courseId, assignmentId, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        return View("Form", posted is null ? result.Value : result.Value with { Input = posted });
    }
}

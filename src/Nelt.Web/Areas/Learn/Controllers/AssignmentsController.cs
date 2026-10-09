using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Common;
using Nelt.Application.Features.Assignments;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Learn.Controllers;

[Route("learn/courses/{courseId:int}/assignments")]
public sealed class AssignmentsController(IStudentAssignmentService assignments) : LearnController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await assignments.ListAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpGet("{assignmentId:int}")]
    public async Task<IActionResult> Show(int courseId, int assignmentId)
    {
        var result = await assignments.GetAsync(courseId, assignmentId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpPost("{assignmentId:int}")]
    [RequestSizeLimit(60 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 60 * 1024 * 1024)]
    public async Task<IActionResult> Submit(int courseId, int assignmentId, [Bind(Prefix = "Input")] SubmitInput input, IFormFile? file)
    {
        if (ModelState.IsValid)
        {
            var result = await assignments.SubmitAsync(courseId, assignmentId, input, file.ToUpload(), Aborted);
            if (result.Succeeded)
            {
                Flash("Your work was submitted.");
                return RedirectToAction(nameof(Show), new { courseId, assignmentId });
            }

            if (!TryAddFormError(result.Error!, prefix: null))
            {
                return Failure(result.Error!);
            }
        }

        var page = await assignments.GetAsync(courseId, assignmentId, Aborted);
        return page.Failed ? Failure(page.Error!) : View(nameof(Show), page.Value);
    }
}

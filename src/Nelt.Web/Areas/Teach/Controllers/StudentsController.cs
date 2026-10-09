using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Progress;

namespace Nelt.Web.Areas.Teach.Controllers;

[Route("teach/courses/{courseId:int}/students")]
public sealed class StudentsController(IProgressService progress) : TeachController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await progress.RosterAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpGet("{enrollmentId:int}")]
    public async Task<IActionResult> Show(int courseId, int enrollmentId)
    {
        var result = await progress.StudentAsync(courseId, enrollmentId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }
}

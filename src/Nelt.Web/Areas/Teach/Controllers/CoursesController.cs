using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Dashboard;

namespace Nelt.Web.Areas.Teach.Controllers;

[Route("teach")]
public sealed class CoursesController(IDashboardService dashboard) : TeachController
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await dashboard.TeachingAsync(Aborted));

    [HttpGet("courses/{courseId:int}")]
    public async Task<IActionResult> Overview(int courseId)
    {
        var result = await dashboard.CourseOverviewAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }
}

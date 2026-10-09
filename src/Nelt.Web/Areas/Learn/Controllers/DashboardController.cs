using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Learning;

namespace Nelt.Web.Areas.Learn.Controllers;

[Route("learn")]
public sealed class DashboardController(ILearningService learning) : LearnController
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await learning.DashboardAsync(Aborted));

    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule() => View(await learning.ScheduleAsync(Aborted));
}

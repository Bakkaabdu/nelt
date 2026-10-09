using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Attendance;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Teach.Controllers;

public sealed record SessionsPage(SessionList List, SessionSeriesInput Series);

[Route("teach/courses/{courseId:int}/sessions")]
public sealed class SessionsController(IClassSessionService sessions) : TeachController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await sessions.ListAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(new SessionsPage(result.Value, new SessionSeriesInput()));
    }

    [HttpPost("series")]
    public async Task<IActionResult> CreateSeries(int courseId, [Bind(Prefix = "Series")] SessionSeriesInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await sessions.CreateSeriesAsync(courseId, input, Aborted);
            if (result.Failed)
            {
                return Failure(result.Error!);
            }

            TempData[FlashMessages.MessageKey] = string.Format(System.Globalization.CultureInfo.CurrentCulture, L["{0} sessions were added to the schedule."], result.Value);
            TempData[FlashMessages.KindKey] = "success";
            return RedirectToAction(nameof(Index), new { courseId });
        }

        var list = await sessions.ListAsync(courseId, Aborted);
        return list.Failed ? Failure(list.Error!) : View(nameof(Index), new SessionsPage(list.Value, input));
    }

    [HttpGet("new")]
    public Task<IActionResult> Create(int courseId) => FormAsync(courseId, null, null);

    [HttpPost("new")]
    public async Task<IActionResult> Create(int courseId, [Bind(Prefix = "Input")] SessionInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await sessions.CreateAsync(courseId, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The session was added.");
                return RedirectToAction(nameof(Index), new { courseId });
            }

            return Failure(result.Error!);
        }

        return await FormAsync(courseId, null, input);
    }

    [HttpGet("{sessionId:int}/edit")]
    public Task<IActionResult> Edit(int courseId, int sessionId) => FormAsync(courseId, sessionId, null);

    [HttpPost("{sessionId:int}/edit")]
    public async Task<IActionResult> Edit(int courseId, int sessionId, [Bind(Prefix = "Input")] SessionInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await sessions.UpdateAsync(courseId, sessionId, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The session was saved.");
                return RedirectToAction(nameof(Index), new { courseId });
            }

            return Failure(result.Error!);
        }

        return await FormAsync(courseId, sessionId, input);
    }

    [HttpPost("{sessionId:int}/delete")]
    public async Task<IActionResult> Delete(int courseId, int sessionId)
        => RedirectWithResult(await sessions.DeleteAsync(courseId, sessionId, Aborted), "The session was removed.", nameof(Index), new { courseId });

    [HttpGet("{sessionId:int}")]
    public async Task<IActionResult> Attendance(int courseId, int sessionId)
    {
        var result = await sessions.SheetAsync(courseId, sessionId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpPost("{sessionId:int}")]
    public async Task<IActionResult> Attendance(int courseId, int sessionId, List<AttendanceMark> marks)
        => RedirectWithResult(await sessions.SaveSheetAsync(courseId, sessionId, marks, Aborted), "Attendance was saved.", nameof(Attendance), new { courseId, sessionId });

    [HttpGet("~/teach/courses/{courseId:int}/attendance")]
    public async Task<IActionResult> Matrix(int courseId)
    {
        var result = await sessions.MatrixAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    private async Task<IActionResult> FormAsync(int courseId, int? sessionId, SessionInput? posted)
    {
        var result = await sessions.GetForEditAsync(courseId, sessionId, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        return View("Form", posted is null ? result.Value : result.Value with { Input = posted });
    }
}

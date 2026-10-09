using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Events;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Teach.Controllers;

public sealed record EventsPage(IReadOnlyList<EventRow> Events, bool Past);

[Route("teach/events")]
public sealed class EventsController(IEventService events) : TeachController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(bool past = false) => View(new EventsPage(await events.ListAsync(past, Aborted), past));

    [HttpGet("new")]
    public Task<IActionResult> Create() => FormAsync(null, null);

    [HttpPost("new")]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] EventInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await events.CreateAsync(input, Aborted);
            if (result.Succeeded)
            {
                Flash("The event was published.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(null, input);
    }

    [HttpGet("{id:int}")]
    public Task<IActionResult> Edit(int id) => FormAsync(id, null);

    [HttpPost("{id:int}")]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] EventInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await events.UpdateAsync(id, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The event was saved.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(id, input);
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id)
        => RedirectWithResult(await events.DeleteAsync(id, Aborted), "The event was deleted.", nameof(Index));

    private async Task<IActionResult> FormAsync(int? id, EventInput? posted)
    {
        var result = await events.GetForEditAsync(id, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        return View("Form", posted is null ? result.Value : result.Value with { Input = posted });
    }
}

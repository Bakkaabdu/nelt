using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Levels;

namespace Nelt.Web.Areas.Admin.Controllers;

public sealed record LevelFormModel(int? Id, LevelInput Input);

public sealed class LevelsController(ILevelService levels) : AdminController
{
    public async Task<IActionResult> Index() => View(await levels.ListAsync(Aborted));

    [HttpGet]
    public IActionResult Create() => View("Form", new LevelFormModel(null, new LevelInput()));

    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] LevelInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await levels.CreateAsync(input, Aborted);
            if (result.Succeeded)
            {
                Flash("The level was created.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return View("Form", new LevelFormModel(null, input));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var input = await levels.GetForEditAsync(id, Aborted);
        return input is null ? NotFound() : View("Form", new LevelFormModel(id, input));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] LevelInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await levels.UpdateAsync(id, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The level was saved.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return View("Form", new LevelFormModel(id, input));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
        => RedirectWithResult(await levels.DeleteAsync(id, Aborted), "The level was deleted.", nameof(Index));
}

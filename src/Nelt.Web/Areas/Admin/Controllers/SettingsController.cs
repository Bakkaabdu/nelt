using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Settings;

namespace Nelt.Web.Areas.Admin.Controllers;

public sealed class SettingsController(IPlatformSettingsService settings) : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await settings.GetForEditAsync(Aborted));

    [HttpPost]
    public async Task<IActionResult> Index([Bind(Prefix = "")] SettingsInput input)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var result = await settings.UpdateAsync(input, Aborted);
        if (result.Failed)
        {
            if (!TryAddFormError(result.Error!, prefix: null))
            {
                return Failure(result.Error!);
            }

            return View(input);
        }

        Flash("The platform information was saved.");
        return RedirectToAction(nameof(Index));
    }
}

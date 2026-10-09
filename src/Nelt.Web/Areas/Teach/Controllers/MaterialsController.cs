using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Materials;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Teach.Controllers;

public sealed record MaterialsPage(MaterialLibrary Library, MaterialInput Upload);

public sealed record MaterialEditPage(int Id, MaterialInput Input, MaterialLibrary Library);

[Route("teach/materials")]
public sealed class MaterialsController(IMaterialService materials) : TeachController
{
    private const long MaxUpload = 210L * 1024 * 1024;

    [HttpGet("")]
    public async Task<IActionResult> Index(int? level)
    {
        var library = await materials.LibraryAsync(level, Aborted);
        return View(new MaterialsPage(library, new MaterialInput { LevelId = library.Selected?.Id ?? 0 }));
    }

    [HttpPost("")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<IActionResult> Upload([Bind(Prefix = "Upload")] MaterialInput input, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("file", L["Please choose a file."]);
        }

        if (ModelState.IsValid)
        {
            var result = await materials.UploadAsync(input, file.ToUpload()!, Aborted);
            if (result.Succeeded)
            {
                Flash("The file was added to the library.");
                return RedirectToAction(nameof(Index), new { level = input.LevelId });
            }

            if (!TryAddFormError(result.Error!, "Upload"))
            {
                return Failure(result.Error!);
            }
        }

        var library = await materials.LibraryAsync(input.LevelId, Aborted);
        return View(nameof(Index), new MaterialsPage(library, input));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var result = await materials.GetForEditAsync(id, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        return View(new MaterialEditPage(id, result.Value, await materials.LibraryAsync(result.Value.LevelId, Aborted)));
    }

    [HttpPost("{id:int}")]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] MaterialInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await materials.UpdateAsync(id, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The file details were saved.");
                return RedirectToAction(nameof(Index), new { level = input.LevelId });
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return View(new MaterialEditPage(id, input, await materials.LibraryAsync(input.LevelId, Aborted)));
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, int? level)
        => RedirectWithResult(await materials.DeleteAsync(id, Aborted), "The file was removed.", nameof(Index), new { level });

    [HttpPost("{id:int}/move")]
    public async Task<IActionResult> Move(int id, int direction, int? level)
    {
        var result = await materials.MoveAsync(id, direction, Aborted);
        return result.Failed ? Failure(result.Error!) : RedirectToAction(nameof(Index), new { level });
    }
}

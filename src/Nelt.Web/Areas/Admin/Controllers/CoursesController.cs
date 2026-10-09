using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Courses;
using Nelt.Application.Features.Levels;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Admin.Controllers;

public sealed record CourseFormModel(
    int? Id,
    CourseInput Input,
    string? CoverImageKey,
    int EnrollmentCount,
    IReadOnlyList<LevelOption> Levels,
    IReadOnlyList<UserOption> Instructors);

public sealed class CoursesController(ICourseAdminService courses, ILevelService levels) : AdminController
{
    private const long MaxUpload = 6 * 1024 * 1024;

    public async Task<IActionResult> Index() => View(await courses.ListAsync(Aborted));

    [HttpGet]
    public Task<IActionResult> Create() => FormAsync(null, new CourseInput(), null, 0);

    [HttpPost]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] CourseInput input, IFormFile? cover)
    {
        if (ModelState.IsValid)
        {
            var result = await courses.CreateAsync(input, cover.ToUpload(), Aborted);
            if (result.Succeeded)
            {
                Flash("The course was created.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(null, input, null, 0);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await courses.GetForEditAsync(id, Aborted);
        return model is null ? NotFound() : await FormAsync(id, model.Input, model.CoverImageKey, model.EnrollmentCount);
    }

    [HttpPost]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] CourseInput input, IFormFile? cover, bool removeCover)
    {
        if (ModelState.IsValid)
        {
            var result = await courses.UpdateAsync(id, input, cover.ToUpload(), removeCover, Aborted);
            if (result.Succeeded)
            {
                Flash("The course was saved.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        var current = await courses.GetForEditAsync(id, Aborted);
        return current is null ? NotFound() : await FormAsync(id, input, current.CoverImageKey, current.EnrollmentCount);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
        => RedirectWithResult(await courses.DeleteAsync(id, Aborted), "The course was deleted.", nameof(Index));

    private async Task<IActionResult> FormAsync(int? id, CourseInput input, string? cover, int enrollments)
        => View("Form", new CourseFormModel(id, input, cover, enrollments, await levels.OptionsAsync(Aborted), await courses.InstructorsAsync(Aborted)));
}

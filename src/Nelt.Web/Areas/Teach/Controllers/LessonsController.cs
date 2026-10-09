using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Common;
using Nelt.Application.Features.Lessons;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Teach.Controllers;

[Route("teach/courses/{courseId:int}/lessons")]
public sealed class LessonsController(ILessonService lessons) : TeachController
{
    private const long MaxUpload = 2L * 1024 * 1024 * 1024 + 10 * 1024 * 1024;

    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await lessons.ListAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpGet("new")]
    public Task<IActionResult> Create(int courseId) => FormAsync(courseId, null, null);

    [HttpPost("new")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<IActionResult> Create(int courseId, [Bind(Prefix = "Input")] LessonInput input, IFormFile? video)
    {
        if (ModelState.IsValid)
        {
            var result = await lessons.CreateAsync(courseId, input, video.ToUpload(), Aborted);
            if (result.Succeeded)
            {
                Flash("The lesson was created.");
                return RedirectToAction(nameof(Index), new { courseId });
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(courseId, null, input);
    }

    [HttpGet("{lessonId:int}")]
    public Task<IActionResult> Edit(int courseId, int lessonId) => FormAsync(courseId, lessonId, null);

    [HttpPost("{lessonId:int}")]
    [RequestSizeLimit(MaxUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUpload)]
    public async Task<IActionResult> Edit(int courseId, int lessonId, [Bind(Prefix = "Input")] LessonInput input, IFormFile? video, bool removeVideo)
    {
        if (ModelState.IsValid)
        {
            var result = await lessons.UpdateAsync(courseId, lessonId, input, video.ToUpload(), removeVideo, Aborted);
            if (result.Succeeded)
            {
                Flash("The lesson was saved.");
                return RedirectToAction(nameof(Index), new { courseId });
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return await FormAsync(courseId, lessonId, input);
    }

    [HttpPost("{lessonId:int}/delete")]
    public async Task<IActionResult> Delete(int courseId, int lessonId)
        => RedirectWithResult(await lessons.DeleteAsync(courseId, lessonId, Aborted), "The lesson was deleted.", nameof(Index), new { courseId });

    [HttpPost("{lessonId:int}/move")]
    public async Task<IActionResult> Move(int courseId, int lessonId, int direction)
    {
        var result = await lessons.MoveAsync(courseId, lessonId, direction, Aborted);
        return result.Failed ? Failure(result.Error!) : RedirectToAction(nameof(Index), new { courseId });
    }

    private async Task<IActionResult> FormAsync(int courseId, int? lessonId, LessonInput? posted)
    {
        var result = await lessons.GetForEditAsync(courseId, lessonId, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        var model = posted is null ? result.Value : result.Value with { Input = posted };
        return View("Form", model);
    }
}

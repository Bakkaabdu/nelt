using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Learning;

namespace Nelt.Web.Areas.Learn.Controllers;

[Route("learn/courses/{courseId:int}")]
public sealed class CourseController(ILearningService learning) : LearnController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await learning.CourseHomeAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpGet("lessons/{lessonId:int}")]
    public async Task<IActionResult> Lesson(int courseId, int lessonId)
    {
        var result = await learning.LessonAsync(courseId, lessonId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    /// <summary>Marks a lesson as done. Called by the form button and by the player when the video ends (fetch).</summary>
    [HttpPost("lessons/{lessonId:int}/complete")]
    public async Task<IActionResult> Complete(int courseId, int lessonId, int? next)
    {
        var result = await learning.CompleteLessonAsync(courseId, lessonId, Aborted);
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.Ordinal))
        {
            return result.Failed ? Failure(result.Error!) : NoContent();
        }

        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        return next is { } nextLesson
            ? RedirectToAction(nameof(Lesson), new { courseId, lessonId = nextLesson })
            : RedirectToAction(nameof(Index), new { courseId });
    }

    [HttpGet("materials")]
    public async Task<IActionResult> Materials(int courseId)
    {
        var result = await learning.MaterialsAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance(int courseId)
    {
        var result = await learning.AttendanceAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }
}

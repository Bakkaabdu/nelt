using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Catalog;
using Nelt.Application.Features.Enrollments;
using Nelt.Application.Features.Learning;
using Nelt.Domain.Enums;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

[Route("courses")]
public sealed class CoursesController(ICatalogService catalog, IEnrollmentService enrollments, ILearningService learning) : AppController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(TargetLanguage? language, int? level, DeliveryMode? mode)
        => View(await catalog.SearchAsync(new CourseFilter(language, level, mode), Aborted));

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug)
    {
        var course = await catalog.GetCourseAsync(slug, Aborted);
        return course is null ? NotFound() : View(course);
    }

    [HttpGet("{slug}/preview/{lessonId:int}")]
    public async Task<IActionResult> Preview(string slug, int lessonId)
    {
        var result = await learning.PreviewAsync(slug, lessonId, Aborted);
        return result.Failed ? Failure(result.Error!) : View("~/Areas/Learn/Views/Course/Lesson.cshtml", result.Value);
    }

    [HttpPost("{slug}/enroll")]
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Enroll(string slug, StudyMode mode)
    {
        var course = await catalog.GetCourseAsync(slug, Aborted);
        if (course is null)
        {
            return NotFound();
        }

        var result = await enrollments.RequestAsync(course.Card.Id, mode, Aborted);
        if (result.Failed)
        {
            Flash(result.Error!.Message, FlashKind.Error);
            return RedirectToAction(nameof(Details), new { slug });
        }

        Flash("Your seat is reserved. It will be activated as soon as your payment is confirmed.");
        return RedirectToAction(nameof(Details), new { slug });
    }
}

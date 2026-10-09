using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Files;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

/// <summary>
/// Streams protected files (lesson videos, materials, attachments, submissions) after an authorization check.
/// Supports HTTP range requests so video/audio can seek without downloading everything.
/// </summary>
[Route("files")]
public sealed class FilesController(IFileAccessService access, IFileStorage storage) : AppController
{
    [HttpGet("video/{lessonId:int}")]
    [AllowAnonymous] // Free preview lessons are public; everything else is checked by the access service.
    public Task<IActionResult> Video(int lessonId) => ServeAsync(ProtectedFileKind.LessonVideo, lessonId, download: false);

    [Authorize]
    [HttpGet("material/{id:int}")]
    public Task<IActionResult> Material(int id, bool download = false) => ServeAsync(ProtectedFileKind.Material, id, download);

    [Authorize]
    [HttpGet("assignment/{id:int}")]
    public Task<IActionResult> Assignment(int id) => ServeAsync(ProtectedFileKind.AssignmentAttachment, id, download: true);

    [Authorize]
    [HttpGet("submission/{id:int}")]
    public Task<IActionResult> Submission(int id) => ServeAsync(ProtectedFileKind.Submission, id, download: true);

    private async Task<IActionResult> ServeAsync(ProtectedFileKind kind, int id, bool download)
    {
        var descriptor = await access.ResolveAsync(kind, id, Aborted);
        if (descriptor is null)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(descriptor.Key, Aborted);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=3600";
        var inline = !download && FilePolicy.IsInlineSafe(descriptor.ContentType);
        return inline
            ? File(stream, descriptor.ContentType, enableRangeProcessing: true)
            : File(stream, descriptor.ContentType, descriptor.FileName, enableRangeProcessing: true);
    }
}

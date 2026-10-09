using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Application.Features.Files;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

/// <summary>Public, cacheable images (course covers). Keys change on every upload, so long caching is safe.</summary>
[Route("media")]
public sealed class MediaController(IFileAccessService files, IFileStorage storage) : AppController
{
    [HttpGet("cover/{courseId:int}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Cover(int courseId)
    {
        var key = await files.CoverKeyAsync(courseId, Aborted);
        if (key is null)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(key, Aborted);
        return stream is null ? NotFound() : File(stream, FilePolicy.ContentTypeFor(key));
    }
}

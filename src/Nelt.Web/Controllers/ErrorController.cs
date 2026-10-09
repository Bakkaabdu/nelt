using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

public sealed record ErrorViewModel(int StatusCode, string? RequestId);

[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ErrorController(ILogger<ErrorController> logger) : AppController
{
    [Route("error/{statusCode:int?}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Index(int? statusCode)
    {
        var code = statusCode ?? StatusCodes.Status500InternalServerError;
        if (HttpContext.Features.Get<IExceptionHandlerPathFeature>() is { } failure)
        {
            logger.LogError(failure.Error, "Unhandled exception on {Path}", failure.Path);
        }

        var originalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath ?? Request.Path.Value ?? string.Empty;
        if (originalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase) || originalPath.StartsWith("/iclock", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(statusCode: code);
        }

        Response.StatusCode = code;
        return View(new ErrorViewModel(code, HttpContext.TraceIdentifier));
    }
}

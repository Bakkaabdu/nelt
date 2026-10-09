using Nelt.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Nelt.Application.Common;
using Nelt.Web.Infrastructure.Localization;

namespace Nelt.Web.Infrastructure.Mvc;

/// <summary>Shared helpers for turning application results into pages, flashes and form errors.</summary>
public abstract class AppController : Controller
{
    private IStringLocalizer<SharedResource>? _localizer;

    protected IStringLocalizer<SharedResource> L => _localizer ??= HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();

    protected CancellationToken Aborted => HttpContext.RequestAborted;

    protected void Flash(string englishMessage, FlashKind kind = FlashKind.Success)
    {
        TempData[FlashMessages.MessageKey] = L[englishMessage].Value;
        TempData[FlashMessages.KindKey] = kind.ToString().ToLowerInvariant();
    }

    /// <summary>Maps an unexpected-but-expected failure (not found, forbidden) to the right status page.</summary>
    protected IActionResult Failure(Error error) => error.Kind switch
    {
        ErrorKind.NotFound => NotFound(),
        ErrorKind.Forbidden => Forbid(),
        _ => BadRequest(),
    };

    /// <summary>For form posts: validation/conflict errors go back to the form; not found / forbidden become status pages.</summary>
    protected bool TryAddFormError(Error error, string? prefix = "Input")
    {
        if (error.Kind is ErrorKind.NotFound or ErrorKind.Forbidden)
        {
            return false;
        }

        var key = error.Field is null ? string.Empty : string.IsNullOrEmpty(prefix) ? error.Field : $"{prefix}.{error.Field}";
        ModelState.AddModelError(key, L[error.Message]);
        return true;
    }

    /// <summary>For simple POST actions that redirect back: shows the outcome as a flash message.</summary>
    protected IActionResult RedirectWithResult(Result result, string successMessage, string action, object? routeValues = null)
    {
        if (result.Failed)
        {
            if (result.Error!.Kind is ErrorKind.NotFound or ErrorKind.Forbidden)
            {
                return Failure(result.Error);
            }

            Flash(result.Error.Message, FlashKind.Error);
        }
        else
        {
            Flash(successMessage);
        }

        return RedirectToAction(action, routeValues);
    }

    protected IActionResult LocalRedirectOr(string? returnUrl, string action, string controller, object? routeValues = null)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(action, controller, routeValues);
}

public enum FlashKind
{
    Success,
    Error,
    Info,
}

public static class FlashMessages
{
    public const string MessageKey = "flash.message";
    public const string KindKey = "flash.kind";
}

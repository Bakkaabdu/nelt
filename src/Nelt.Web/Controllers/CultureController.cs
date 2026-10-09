using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Nelt.Domain.Entities;
using Nelt.Web.Infrastructure.Localization;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

[Route("culture")]
public sealed class CultureController(UserManager<ApplicationUser> users) : AppController
{
    [HttpPost("")]
    public async Task<IActionResult> Set(string culture, string? returnUrl)
    {
        if (Cultures.IsSupported(culture))
        {
            Response.Cookies.Append(
                Cultures.CookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true, Secure = Request.IsHttps });

            // Remember the choice on the account so it follows the user to other devices.
            if (User.Identity?.IsAuthenticated == true && await users.GetUserAsync(User) is { } user && user.PreferredLanguage != culture)
            {
                user.PreferredLanguage = culture;
                await users.UpdateAsync(user);
            }
        }

        return LocalRedirectOr(returnUrl, "Index", "Home");
    }
}

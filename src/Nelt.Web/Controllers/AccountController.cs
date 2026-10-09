using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Web.Infrastructure;
using Nelt.Web.Infrastructure.Localization;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

public sealed class LoginInput
{
    [Required, EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; } = true;
}

public sealed class RegisterInput
{
    [Required, StringLength(120), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(160), Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(40), Display(Name = "Phone")]
    public string? PhoneNumber { get; set; }

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordInput
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record AccountFormModel<T>(T Input, string? ReturnUrl);

[Route("account")]
public sealed class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, ILogger<AccountController> logger)
    : AppController
{
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl) => View(new AccountFormModel<LoginInput>(new LoginInput(), returnUrl));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(WebSetup.AuthRateLimit)]
    public async Task<IActionResult> Login([Bind(Prefix = "Input")] LoginInput input, string? returnUrl)
    {
        if (!ModelState.IsValid)
        {
            return View(new AccountFormModel<LoginInput>(input, returnUrl));
        }

        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is { IsActive: true })
        {
            var result = await signIn.PasswordSignInAsync(user, input.Password, input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                logger.LogInformation("User {UserId} signed in", user.Id);
                ApplyPreferredLanguage(user);
                return string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl)
                    ? Redirect(await HomeForAsync(user))
                    : LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, L["Too many failed attempts. Please try again in a few minutes."]);
                return View(new AccountFormModel<LoginInput>(input, returnUrl));
            }
        }

        ModelState.AddModelError(string.Empty, L["The email or password is incorrect."]);
        return View(new AccountFormModel<LoginInput>(input, returnUrl));
    }

    [HttpGet("register")]
    [AllowAnonymous]
    public IActionResult Register(string? returnUrl) => View(new AccountFormModel<RegisterInput>(new RegisterInput(), returnUrl));

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(WebSetup.AuthRateLimit)]
    public async Task<IActionResult> Register([Bind(Prefix = "Input")] RegisterInput input, string? returnUrl)
    {
        if (!ModelState.IsValid)
        {
            return View(new AccountFormModel<RegisterInput>(input, returnUrl));
        }

        var user = new ApplicationUser
        {
            UserName = input.Email.Trim(),
            Email = input.Email.Trim(),
            FullName = input.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim(),
            PreferredLanguage = Cultures.Current.Code,
        };

        var created = await users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
            {
                var key = error.Code.Contains("Password", StringComparison.Ordinal) ? "Input.Password" : error.Code.Contains("Email", StringComparison.Ordinal) || error.Code.Contains("UserName", StringComparison.Ordinal) ? "Input.Email" : string.Empty;
                ModelState.AddModelError(key, IdentityMessage(error));
            }

            return View(new AccountFormModel<RegisterInput>(input, returnUrl));
        }

        await users.AddToRoleAsync(user, Roles.Student);
        await signIn.SignInAsync(user, isPersistent: true);
        logger.LogInformation("New student {UserId} registered", user.Id);
        Flash("Welcome to Nelt! Choose a course to get started.");
        return LocalRedirectOr(returnUrl, "Index", "Courses");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("access-denied")]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    [HttpGet("password")]
    [Authorize]
    public IActionResult Password() => View(new AccountFormModel<ChangePasswordInput>(new ChangePasswordInput(), null));

    [HttpPost("password")]
    [Authorize]
    [EnableRateLimiting(WebSetup.AuthRateLimit)]
    public async Task<IActionResult> Password([Bind(Prefix = "Input")] ChangePasswordInput input)
    {
        if (!ModelState.IsValid)
        {
            return View(new AccountFormModel<ChangePasswordInput>(input, null));
        }

        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code == "PasswordMismatch" ? "Input.CurrentPassword" : "Input.NewPassword", IdentityMessage(error));
            }

            return View(new AccountFormModel<ChangePasswordInput>(input, null));
        }

        await signIn.RefreshSignInAsync(user);
        Flash("Your password was changed.");
        return Redirect(await HomeForAsync(user));
    }

    private async Task<string> HomeForAsync(ApplicationUser user)
    {
        if (await users.IsInRoleAsync(user, Roles.Admin))
        {
            return Url.Action("Index", "Dashboard", new { area = "Admin" })!;
        }

        return await users.IsInRoleAsync(user, Roles.Instructor)
            ? Url.Action("Index", "Courses", new { area = "Teach" })!
            : Url.Action("Index", "Dashboard", new { area = "Learn" })!;
    }

    private void ApplyPreferredLanguage(ApplicationUser user)
    {
        if (Cultures.IsSupported(user.PreferredLanguage))
        {
            Response.Cookies.Append(Cultures.CookieName, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true, Secure = Request.IsHttps });
        }
    }

    /// <summary>Identity's own messages are English-only; map the common ones to translatable keys.</summary>
    private string IdentityMessage(IdentityError error) => error.Code switch
    {
        "DuplicateEmail" or "DuplicateUserName" => L["An account with this email already exists."],
        "PasswordTooShort" => L["The password must be at least 8 characters long."],
        "PasswordRequiresDigit" => L["The password must contain at least one digit."],
        "PasswordRequiresLower" => L["The password must contain at least one lowercase letter."],
        "PasswordMismatch" => L["The current password is incorrect."],
        "InvalidEmail" => L["Please enter a valid email address."],
        _ => error.Description,
    };
}

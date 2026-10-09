using Nelt.Web;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Web.Infrastructure.Localization;
using Nelt.Web.Infrastructure.Mvc;
using Nelt.Web.Infrastructure.Security;

namespace Nelt.Web.Infrastructure;

public static class WebSetup
{
    public const string AuthRateLimit = "auth";
    public const string DeviceRateLimit = "device";

    public static IServiceCollection AddWeb(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddOptions<AttendanceOptions>()
            .Bind(configuration.GetSection(AttendanceOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(o =>
        {
            var cultures = Cultures.Supported.Select(c => new CultureInfo(c.Code)).ToList();
            o.DefaultRequestCulture = new RequestCulture(Cultures.Default);
            o.SupportedCultures = cultures;
            o.SupportedUICultures = cultures;
            o.FallBackToParentCultures = true;
            o.FallBackToParentUICultures = true;
            o.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new CookieRequestCultureProvider { CookieName = Cultures.CookieName },
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        services.AddControllersWithViews(o =>
            {
                o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
                o.Filters.Add<LocalizeModelStateFilter>();
                o.ModelMetadataDetailsProviders.Add(new DefaultValidationMessagesProvider());

                // Numbers/dates from <input type=number|date> are culture-invariant; bind them that way in every UI language.
                var index = o.ValueProviderFactories.IndexOf(o.ValueProviderFactories.OfType<FormValueProviderFactory>().Single());
                o.ValueProviderFactories[index] = new InvariantFormValueProviderFactory();
            })
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));
        services.ConfigureOptions<ConfigureModelBindingMessages>();

        services.AddRouting(o => o.LowercaseUrls = true);
        services.AddProblemDetails();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "nelt.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.LoginPath = "/account/login";
            o.LogoutPath = "/account/logout";
            o.AccessDeniedPath = "/account/access-denied";
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            o.Events = new CookieAuthenticationEvents
            {
                // API callers get status codes, not login redirects.
                OnRedirectToLogin = ctx => RedirectOrStatus(ctx, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = ctx => RedirectOrStatus(ctx, StatusCodes.Status403Forbidden),
            };
        });
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = "nelt.af";
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(DeviceRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        // Large lesson videos are uploaded through the platform; the per-action limits are enforced with attributes.
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = FilePolicy.MaxBytes(FileCategory.Video) + 1024 * 1024);

        if (!environment.IsDevelopment())
        {
            services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(180));
        }

        return services;
    }

    public static WebApplication UseWebPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/error/{0}");
        app.UseMiddleware<SecurityHeadersMiddleware>();
        // Many fingerprint terminals can only speak plain HTTP on the local network; optionally exempt their endpoint.
        var allowPlainHttpDevices = app.Configuration.GetValue<bool>("Devices:AllowPlainHttp");
        app.UseWhen(
            ctx => !(allowPlainHttpDevices && ctx.Request.Path.StartsWithSegments("/iclock")),
            branch => branch.UseHttpsRedirection());
        app.UseRequestLocalization();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapStaticAssets();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");
        app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}").WithStaticAssets();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
        return app;
    }

    private static Task RedirectOrStatus(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
}

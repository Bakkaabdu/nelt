using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelt.Application.Features.Settings;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminName { get; set; } = "Administrator";
}

/// <summary>Brings the schema up to date and ensures the reference data every installation needs.</summary>
public static class DatabaseInitializer
{
    private const int MaxAttempts = 10;

    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var db = provider.GetRequiredService<AppDbContext>();

        await ApplySchemaWithRetryAsync(db, logger, ct);
        await SeedRolesAsync(provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), ct);
        await SeedReferenceDataAsync(db, logger, ct);
        await SeedAdministratorAsync(provider, logger);

        // Warm the settings cache; this also applies the configured platform time zone.
        await provider.GetRequiredService<IPlatformSettingsService>().GetAsync(ct);
    }

    private static async Task ApplySchemaWithRetryAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (db.Database.GetMigrations().Any())
                {
                    await db.Database.MigrateAsync(ct);
                }
                else
                {
                    // No migrations generated yet (first run in development): create the schema from the model.
                    logger.LogWarning("No EF Core migrations found; creating the schema directly. Generate migrations before going to production.");
                    await db.Database.EnsureCreatedAsync(ct);
                }

                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is not OperationCanceledException)
            {
                // Typical when the database container is still starting.
                var delay = TimeSpan.FromSeconds(Math.Min(30, attempt * 3));
                logger.LogWarning(ex, "Database not reachable (attempt {Attempt}/{Max}); retrying in {Delay}", attempt, MaxAttempts, delay);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roles, CancellationToken ct)
    {
        foreach (var role in Roles.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role));
            }
        }
    }

    private static async Task SeedReferenceDataAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (!await db.PlatformSettings.AnyAsync(ct))
        {
            db.PlatformSettings.Add(SeedContent.Settings());
            logger.LogInformation("Created default platform settings");
        }

        if (!await db.Levels.AnyAsync(ct))
        {
            db.Levels.AddRange(SeedContent.Levels());
            logger.LogInformation("Created the standard CEFR and HSK levels");
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdministratorAsync(IServiceProvider provider, ILogger logger)
    {
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        var options = provider.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            logger.LogWarning("No administrator exists. Set Seed:AdminEmail and Seed:AdminPassword to create one.");
            return;
        }

        var admin = await users.FindByEmailAsync(options.AdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = options.AdminEmail,
                Email = options.AdminEmail,
                EmailConfirmed = true,
                FullName = options.AdminName,
            };

            var created = await users.CreateAsync(admin, options.AdminPassword);
            if (!created.Succeeded)
            {
                logger.LogError("Could not create the administrator: {Errors}", string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }
        }

        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Administrator {Email} created", options.AdminEmail);
    }
}

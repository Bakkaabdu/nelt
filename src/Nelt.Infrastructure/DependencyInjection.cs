using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Application.Abstractions;
using Nelt.Domain.Entities;
using Nelt.Infrastructure.Background;
using Nelt.Infrastructure.Identity;
using Nelt.Infrastructure.Persistence;
using Nelt.Infrastructure.Storage;

namespace Nelt.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            sql.CommandTimeout(30);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        }));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Lockout.MaxFailedAccessAttempts = 6;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<NeltClaimsPrincipalFactory>();

        // Shared key ring so any instance can read cookies and antiforgery tokens issued by another.
        services.AddDataProtection().SetApplicationName("Nelt").PersistKeysToDbContext<AppDbContext>();

        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.Section));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddHostedService<AttendanceFinalizerWorker>();
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
        return services;
    }
}

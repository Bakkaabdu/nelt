using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nelt.Application.Abstractions;
using Nelt.Infrastructure.Background;

namespace Nelt.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real application (real SQL Server, real EF Core queries, real MVC pipeline and views) in memory.
/// Connection string, administrator and storage folder come from environment variables set by <see cref="NeltFixture"/>.
/// </summary>
public sealed class NeltFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development shows the full exception page, so a failing page reports the real exception in the test output.
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddScoped<ICurrentUser, TestCurrentUser>();
            services.AddTransient<IStartupFilter, TestClientIpStartupFilter>();

            // The attendance worker runs on a timer; the tests call the finalizer explicitly instead.
            var worker = services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AttendanceFinalizerWorker)).ToList();
            foreach (var descriptor in worker)
            {
                services.Remove(descriptor);
            }
        });
    }
}

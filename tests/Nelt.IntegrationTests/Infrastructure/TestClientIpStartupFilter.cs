using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Nelt.IntegrationTests.Infrastructure;

/// <summary>
/// Gives every test client its own remote IP address. Sign-in is rate limited per IP (10 per minute), and in-memory
/// test requests have no real IP, so without this all test clients would share one limit.
/// </summary>
public sealed class TestClientIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var value) && IPAddress.TryParse(value.ToString(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }

            await nextMiddleware(context);
        });
        next(app);
    };
}

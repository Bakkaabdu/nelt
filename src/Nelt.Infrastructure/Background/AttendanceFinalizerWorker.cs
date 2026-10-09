using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelt.Application.Common;
using Nelt.Application.Features.Attendance;

namespace Nelt.Infrastructure.Background;

/// <summary>Periodically writes absences for ended class sessions. Failures are logged and retried on the next tick.</summary>
public sealed class AttendanceFinalizerWorker(
    IServiceScopeFactory scopes,
    IOptions<AttendanceOptions> options,
    ILogger<AttendanceFinalizerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.FinalizeIntervalMinutes));
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var finalizer = scope.ServiceProvider.GetRequiredService<IAttendanceFinalizer>();
            var absences = await finalizer.FinalizeEndedSessionsAsync(ct);
            if (absences > 0)
            {
                logger.LogInformation("Recorded {Count} absences for ended class sessions", absences);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Attendance finalization failed; it will be retried");
        }
    }
}

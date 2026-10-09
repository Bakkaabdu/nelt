using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nelt.Application.Common;
using Nelt.Application.Features.Attendance;
using Nelt.Web.Infrastructure;

namespace Nelt.Web.Controllers.Api;

public sealed class PunchBatchRequest
{
    [Required, MaxLength(1000)]
    public List<PunchDto> Punches { get; set; } = [];
}

public sealed class PunchDto
{
    /// <summary>User/PIN number enrolled on the terminal.</summary>
    [Required, StringLength(32)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Terminal clock time. Without an offset it is interpreted in the platform time zone.</summary>
    [Required]
    public string Time { get; set; } = string.Empty;
}

/// <summary>
/// JSON push endpoint for fingerprint terminals or a bridge service in front of them.
/// Authenticated with the per-device key shown once in the admin panel (header <c>X-Device-Key</c>).
/// </summary>
[ApiController]
[Route("api/biometric")]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(WebSetup.DeviceRateLimit)]
public sealed class BiometricApiController(IBiometricIngestionService ingestion, IPlatformTime time) : ControllerBase
{
    [HttpPost("punches")]
    [RequestSizeLimit(512 * 1024)]
    [ProducesResponseType<IngestResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Punches([FromBody] PunchBatchRequest request, [FromHeader(Name = "X-Device-Key")] string? deviceKey, CancellationToken ct)
    {
        var device = deviceKey is null ? null : await ingestion.AuthenticateByKeyAsync(deviceKey, ct);
        if (device is null)
        {
            return Unauthorized();
        }

        var punches = new List<RawPunch>(request.Punches.Count);
        foreach (var punch in request.Punches)
        {
            if (!TryParseTime(punch.Time, out var local))
            {
                return ValidationProblem($"Invalid time '{punch.Time}'. Use ISO 8601, e.g. 2026-10-04T17:58:12.");
            }

            punches.Add(new RawPunch(punch.UserId, local));
        }

        return Ok(await ingestion.IngestAsync(device, punches, ct));
    }

    private bool TryParseTime(string value, out DateTime local)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            local = default;
            return false;
        }

        // No offset: already terminal (platform) local time. With "Z" or an offset: normalise through UTC.
        local = parsed.Kind switch
        {
            DateTimeKind.Unspecified => parsed,
            DateTimeKind.Utc => time.ToLocal(parsed),
            _ => time.ToLocal(parsed.ToUniversalTime()),
        };
        return true;
    }
}

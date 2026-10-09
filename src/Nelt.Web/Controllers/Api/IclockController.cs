using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nelt.Application.Common;
using Nelt.Application.Features.Attendance;
using Nelt.Web.Infrastructure;

namespace Nelt.Web.Controllers.Api;

/// <summary>
/// ZKTeco-compatible "ADMS / iclock" push protocol, spoken natively by most fingerprint terminals at course entrances.
/// Point the terminal's Cloud Server setting at this host; it identifies itself by serial number (SN), which must be
/// registered (and active) under Admin → Devices. Attendance logs arrive as tab-separated ATTLOG lines.
/// </summary>
[ApiController]
[Route("iclock")]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(WebSetup.DeviceRateLimit)]
public sealed class IclockController(IBiometricIngestionService ingestion, IPlatformTime time, ILogger<IclockController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>Handshake: the terminal asks for its upload options.</summary>
    [HttpGet("cdata")]
    public async Task<IActionResult> Handshake([FromQuery(Name = "SN")] string? serial, CancellationToken ct)
    {
        var device = await ingestion.AuthenticateBySerialAsync(serial ?? string.Empty, ct);
        if (device is null)
        {
            return Unregistered(serial);
        }

        var offsetHours = time.Zone.GetUtcOffset(time.UtcNow).TotalHours.ToString("0.##", CultureInfo.InvariantCulture);
        var options = new StringBuilder()
            .Append("GET OPTION FROM: ").Append(device.SerialNumber).Append('\n')
            .Append("ATTLOGStamp=None\n")
            .Append("OPERLOGStamp=9999\n")
            .Append("ATTPHOTOStamp=None\n")
            .Append("ErrorDelay=30\n")
            .Append("Delay=10\n")
            .Append("TransTimes=00:00;12:00\n")
            .Append("TransInterval=1\n")
            .Append("TransFlag=TransData AttLog\n")
            .Append("TimeZone=").Append(offsetHours).Append('\n')
            .Append("Realtime=1\n")
            .Append("Encrypt=None\n");
        return Text(options.ToString());
    }

    /// <summary>Data upload. Only ATTLOG (attendance) is processed; other tables are acknowledged and ignored.</summary>
    [HttpPost("cdata")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Upload([FromQuery(Name = "SN")] string? serial, [FromQuery] string? table, CancellationToken ct)
    {
        var device = await ingestion.AuthenticateBySerialAsync(serial ?? string.Empty, ct);
        if (device is null)
        {
            return Unregistered(serial);
        }

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);

        if (!string.Equals(table, "ATTLOG", StringComparison.OrdinalIgnoreCase))
        {
            return Text("OK");
        }

        var punches = ParseAttendanceLog(body);
        var result = await ingestion.IngestAsync(device, punches, ct);
        logger.LogInformation("Terminal {Serial}: {Received} punches, {Recorded} recorded, {Duplicates} duplicates, {Unmatched} unmatched",
            device.SerialNumber, result.Received, result.Recorded, result.Duplicates, result.Unmatched);
        return Text($"OK: {punches.Count}");
    }

    /// <summary>The terminal polls for commands; there are none.</summary>
    [HttpGet("getrequest")]
    public async Task<IActionResult> GetRequest([FromQuery(Name = "SN")] string? serial, CancellationToken ct)
        => await ingestion.AuthenticateBySerialAsync(serial ?? string.Empty, ct) is null ? Unregistered(serial) : Text("OK");

    [HttpPost("devicecmd")]
    public IActionResult DeviceCommand() => Text("OK");

    /// <summary>Parses "PIN\tYYYY-MM-DD HH:MM:SS\tstatus\tverify..." lines; malformed lines are skipped.</summary>
    internal static List<RawPunch> ParseAttendanceLog(string body)
    {
        var punches = new List<RawPunch>();
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            if (DateTime.TryParseExact(parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                punches.Add(new RawPunch(parts[0].Trim(), at));
            }
        }

        return punches;
    }

    private ContentResult Text(string content) => Content(content, "text/plain; charset=utf-8");

    private IActionResult Unregistered(string? serial)
    {
        logger.LogWarning("Rejected terminal with unknown or inactive serial number {Serial}", serial);
        return StatusCode(StatusCodes.Status403Forbidden);
    }
}

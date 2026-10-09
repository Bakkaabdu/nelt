using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Attendance;

public interface IDeviceService
{
    Task<IReadOnlyList<DeviceRow>> ListAsync(CancellationToken ct = default);
    Task<DeviceInput?> GetForEditAsync(int id, CancellationToken ct = default);
    Task<Result<DeviceCredentials>> CreateAsync(DeviceInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(int id, DeviceInput input, CancellationToken ct = default);
    Task<Result<DeviceCredentials>> RotateKeyAsync(int id, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<PagedList<PunchRow>> PunchesAsync(int? deviceId, PunchOutcome? outcome, int page, CancellationToken ct = default);
}

internal sealed class DeviceService(IAppDbContext db, IPlatformTime time) : IDeviceService
{
    public async Task<IReadOnlyList<DeviceRow>> ListAsync(CancellationToken ct = default)
    {
        var todayStart = time.ToUtc(time.LocalToday.ToDateTime(TimeOnly.MinValue));
        return await db.BiometricDevices.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new DeviceRow(d.Id, d.Name, d.SerialNumber, d.Location, d.IsActive, d.LastSeenAt,
                db.BiometricPunches.Count(p => p.DeviceId == d.Id && p.PunchedAt >= todayStart)))
            .ToListAsync(ct);
    }

    public async Task<DeviceInput?> GetForEditAsync(int id, CancellationToken ct = default)
        => await db.BiometricDevices.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new DeviceInput { Name = d.Name, SerialNumber = d.SerialNumber, Location = d.Location, IsActive = d.IsActive })
            .FirstOrDefaultAsync(ct);

    public async Task<Result<DeviceCredentials>> CreateAsync(DeviceInput input, CancellationToken ct = default)
    {
        var serial = input.SerialNumber.Trim();
        if (await db.BiometricDevices.AnyAsync(d => d.SerialNumber == serial, ct))
        {
            return Error.Validation("A device with this serial number is already registered.", nameof(DeviceInput.SerialNumber));
        }

        var key = NewKey();
        var device = new BiometricDevice
        {
            Name = input.Name.Trim(),
            SerialNumber = serial,
            Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim(),
            IsActive = input.IsActive,
            ApiKeyHash = BiometricIngestionService.HashKey(key),
        };

        db.BiometricDevices.Add(device);
        await db.SaveChangesAsync(ct);
        return new DeviceCredentials(device.Id, key);
    }

    public async Task<Result> UpdateAsync(int id, DeviceInput input, CancellationToken ct = default)
    {
        var device = await db.BiometricDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
        {
            return Error.NotFound();
        }

        var serial = input.SerialNumber.Trim();
        if (await db.BiometricDevices.AnyAsync(d => d.SerialNumber == serial && d.Id != id, ct))
        {
            return Error.Validation("A device with this serial number is already registered.", nameof(DeviceInput.SerialNumber));
        }

        device.Name = input.Name.Trim();
        device.SerialNumber = serial;
        device.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        device.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<DeviceCredentials>> RotateKeyAsync(int id, CancellationToken ct = default)
    {
        var device = await db.BiometricDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
        {
            return Error.NotFound();
        }

        var key = NewKey();
        device.ApiKeyHash = BiometricIngestionService.HashKey(key);
        await db.SaveChangesAsync(ct);
        return new DeviceCredentials(device.Id, key);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var device = await db.BiometricDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
        {
            return Error.NotFound();
        }

        if (await db.BiometricPunches.AnyAsync(p => p.DeviceId == id, ct))
        {
            return Error.Conflict("This device already sent fingerprints. Deactivate it instead to keep the attendance history.");
        }

        db.BiometricDevices.Remove(device);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<PagedList<PunchRow>> PunchesAsync(int? deviceId, PunchOutcome? outcome, int page, CancellationToken ct = default)
    {
        var query = db.BiometricPunches.AsNoTracking();
        if (deviceId is { } id)
        {
            query = query.Where(p => p.DeviceId == id);
        }

        if (outcome is { } o)
        {
            query = query.Where(p => p.Outcome == o);
        }

        return query.OrderByDescending(p => p.PunchedAt)
            .Select(p => new PunchRow(p.Id, p.Device!.Name, p.DeviceUserId,
                db.Users.Where(u => u.BiometricId == p.DeviceUserId).Select(u => u.FullName).FirstOrDefault(),
                p.PunchedAt, p.ReceivedAt, p.Outcome))
            .ToPagedListAsync(page, 50, ct);
    }

    private static string NewKey() => "nelt_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

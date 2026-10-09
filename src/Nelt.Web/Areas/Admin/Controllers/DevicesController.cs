using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Common;
using Nelt.Application.Features.Attendance;
using Nelt.Domain.Enums;

namespace Nelt.Web.Areas.Admin.Controllers;

public sealed record DevicesPage(IReadOnlyList<DeviceRow> Devices, DeviceCredentials? NewCredentials, string ServerUrl);

public sealed record DeviceFormModel(int? Id, DeviceInput Input);

public sealed record PunchesPage(PagedList<PunchRow> Punches, IReadOnlyList<DeviceRow> Devices, int? DeviceId, PunchOutcome? Outcome);

public sealed class DevicesController(IDeviceService devices) : AdminController
{
    private const string CredentialsKey = "device.credentials";

    public async Task<IActionResult> Index()
    {
        DeviceCredentials? credentials = null;
        if (TempData[CredentialsKey] is string raw && raw.Split('|') is [var id, var key] && int.TryParse(id, out var deviceId))
        {
            credentials = new DeviceCredentials(deviceId, key);
        }

        return View(new DevicesPage(await devices.ListAsync(Aborted), credentials, $"{Request.Scheme}://{Request.Host}"));
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new DeviceFormModel(null, new DeviceInput()));

    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] DeviceInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await devices.CreateAsync(input, Aborted);
            if (result.Succeeded)
            {
                ShowKeyOnce(result.Value);
                Flash("The device was registered.");
                return RedirectToAction(nameof(Index));
            }

            TryAddFormError(result.Error!);
        }

        return View("Form", new DeviceFormModel(null, input));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var input = await devices.GetForEditAsync(id, Aborted);
        return input is null ? NotFound() : View("Form", new DeviceFormModel(id, input));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] DeviceInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await devices.UpdateAsync(id, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The device was saved.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        return View("Form", new DeviceFormModel(id, input));
    }

    [HttpPost]
    public async Task<IActionResult> RotateKey(int id)
    {
        var result = await devices.RotateKeyAsync(id, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        ShowKeyOnce(result.Value);
        Flash("A new key was generated. The old key no longer works.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
        => RedirectWithResult(await devices.DeleteAsync(id, Aborted), "The device was removed.", nameof(Index));

    public async Task<IActionResult> Punches(int? device, PunchOutcome? outcome, int page = 1)
        => View(new PunchesPage(await devices.PunchesAsync(device, outcome, page, Aborted), await devices.ListAsync(Aborted), device, outcome));

    // The plain key is only ever shown once, right after it is generated (TempData is consumed on the next read).
    private void ShowKeyOnce(DeviceCredentials credentials) => TempData[CredentialsKey] = $"{credentials.DeviceId}|{credentials.ApiKey}";
}

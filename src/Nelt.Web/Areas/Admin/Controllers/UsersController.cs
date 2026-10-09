using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Common;
using Nelt.Application.Features.Users;

namespace Nelt.Web.Areas.Admin.Controllers;

public sealed record UsersPage(PagedList<UserRow> Users, UserFilter Filter);

public sealed class UsersController(IUserAdminService users) : AdminController
{
    public async Task<IActionResult> Index(string? role, string? q, int page = 1)
    {
        var filter = new UserFilter(role, q, page);
        return View(new UsersPage(await users.ListAsync(filter, Aborted), filter));
    }

    [HttpGet]
    public IActionResult Create(string? role)
        => View("Form", new UserEditModel(null, new UserInput { Role = role ?? Domain.Enums.Roles.Student }, false));

    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] UserInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await users.CreateAsync(input, Aborted);
            if (result.Succeeded)
            {
                Flash("The account was created.");
                return RedirectToAction(nameof(Index));
            }

            TryAddFormError(result.Error!);
        }

        return View("Form", new UserEditModel(null, input, false));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await users.GetForEditAsync(id, Aborted);
        return model is null ? NotFound() : View("Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] UserInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await users.UpdateAsync(id, input, Aborted);
            if (result.Succeeded)
            {
                Flash("The account was saved.");
                return RedirectToAction(nameof(Index));
            }

            if (!TryAddFormError(result.Error!))
            {
                return Failure(result.Error!);
            }
        }

        var current = await users.GetForEditAsync(id, Aborted);
        return current is null ? NotFound() : View("Form", current with { Input = input });
    }
}

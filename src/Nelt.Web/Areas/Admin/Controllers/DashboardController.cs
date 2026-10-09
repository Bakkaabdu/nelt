using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Dashboard;

namespace Nelt.Web.Areas.Admin.Controllers;

public sealed class DashboardController(IDashboardService dashboard) : AdminController
{
    public async Task<IActionResult> Index() => View(await dashboard.AdminAsync(Aborted));
}

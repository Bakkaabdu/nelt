using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Catalog;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

public sealed class HomeController(ICatalogService catalog) : AppController
{
    [HttpGet("/")]
    public async Task<IActionResult> Index() => View(await catalog.GetLandingAsync(Aborted));
}

using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Catalog;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

[Route("events")]
public sealed class EventsController(ICatalogService catalog) : AppController
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await catalog.PublicEventsAsync(Aborted));
}

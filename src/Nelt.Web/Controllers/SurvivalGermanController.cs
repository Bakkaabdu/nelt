using Microsoft.AspNetCore.Mvc;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

/// <summary>
/// "Survival German — أنقذ نفسك بالألمانية": a story game for A1/A2 learners. It runs entirely in the browser
/// (wwwroot/js/survival-german.js plays wwwroot/game/missions/*.json) and keeps progress in the browser,
/// so it is open to everyone, signed in or not.
/// </summary>
[Route("survival-german")]
public sealed class SurvivalGermanController : AppController
{
    [HttpGet("")]
    public IActionResult Index() => View();
}

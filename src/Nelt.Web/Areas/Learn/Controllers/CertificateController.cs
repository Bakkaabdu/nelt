using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Certificates;
using Nelt.Application.Features.Settings;

namespace Nelt.Web.Areas.Learn.Controllers;

public sealed record PrintableCertificate(CertificateDocument Document, SiteSettings Site, string VerifyUrl);

[Route("learn/courses/{courseId:int}/certificate")]
public sealed class CertificateController(ICertificateService certificates, IPlatformSettingsService settings) : LearnController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int courseId)
    {
        var result = await certificates.OverviewAsync(courseId, Aborted);
        return result.Failed ? Failure(result.Error!) : View(result.Value);
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestCertificate(int courseId)
        => RedirectWithResult(await certificates.RequestAsync(courseId, Aborted), "Your certificate request was sent to your instructor.", nameof(Index), new { courseId });

    [HttpGet("print")]
    public async Task<IActionResult> Print(int courseId)
    {
        var result = await certificates.MyCertificateAsync(courseId, Aborted);
        if (result.Failed)
        {
            return Failure(result.Error!);
        }

        var verifyUrl = Url.Action("Verify", "Certificates", new { area = string.Empty, serial = result.Value.SerialNumber }, Request.Scheme)!;
        return View(new PrintableCertificate(result.Value, await settings.GetAsync(Aborted), verifyUrl));
    }
}

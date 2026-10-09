using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Certificates;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Controllers;

public sealed record VerifyCertificateModel(string? Serial, CertificateDocument? Certificate);

[Route("certificates")]
public sealed class CertificatesController(ICertificateService certificates) : AppController
{
    [HttpGet("verify")]
    public async Task<IActionResult> Verify(string? serial)
    {
        CertificateDocument? document = null;
        if (!string.IsNullOrWhiteSpace(serial))
        {
            document = await certificates.VerifyAsync(serial, Aborted);
        }

        return View(new VerifyCertificateModel(serial?.Trim(), document));
    }
}

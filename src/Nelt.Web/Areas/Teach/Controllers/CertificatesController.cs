using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Features.Certificates;
using Nelt.Domain.Enums;

namespace Nelt.Web.Areas.Teach.Controllers;

public sealed record CertificateRequestsPage(IReadOnlyList<CertificateRequestRow> Requests, CertificateStatus? Status);

[Route("teach/certificates")]
public sealed class CertificatesController(ICertificateService certificates) : TeachController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CertificateStatus? status = CertificateStatus.Pending)
        => View(new CertificateRequestsPage(await certificates.RequestsAsync(status, Aborted), status));

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CertificateStatus? status)
        => RedirectWithResult(await certificates.ApproveAsync(id, Aborted), "The certificate was issued.", nameof(Index), new { status });

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, ReviewInput input, CertificateStatus? status)
        => RedirectWithResult(await certificates.RejectAsync(id, input, Aborted), "The request was declined.", nameof(Index), new { status });
}

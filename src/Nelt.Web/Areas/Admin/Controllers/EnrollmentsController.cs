using Microsoft.AspNetCore.Mvc;
using Nelt.Application.Common;
using Nelt.Application.Features.Courses;
using Nelt.Application.Features.Enrollments;
using Nelt.Application.Features.Users;
using Nelt.Domain.Enums;

namespace Nelt.Web.Areas.Admin.Controllers;

public sealed record EnrollmentsPage(PagedList<EnrollmentRow> Enrollments, EnrollmentFilter Filter, IReadOnlyList<CourseAdminRow> Courses);

public sealed record ManualEnrollmentPage(ManualEnrollmentInput Input, IReadOnlyList<StudentOption> Students, IReadOnlyList<CourseAdminRow> Courses);

public sealed class EnrollmentsController(IEnrollmentService enrollments, ICourseAdminService courses, IUserAdminService users) : AdminController
{
    public async Task<IActionResult> Index(EnrollmentStatus? status, int? course, string? q, int page = 1)
    {
        var filter = new EnrollmentFilter(status, course, q, page);
        return View(new EnrollmentsPage(await enrollments.ListAsync(filter, Aborted), filter, await courses.ListAsync(Aborted)));
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(int id, ConfirmPaymentInput input, string? returnUrl)
    {
        var result = ModelState.IsValid ? await enrollments.ConfirmAsync(id, input, Aborted) : Result.Fail(Error.Validation("Please enter a valid amount."));
        return Outcome(result, "Payment confirmed — the student now has access.", returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, string? returnUrl)
        => Outcome(await enrollments.CancelAsync(id, Aborted), "The enrollment was cancelled.", returnUrl);

    [HttpPost]
    public async Task<IActionResult> ChangeMode(int id, StudyMode mode, string? returnUrl)
        => Outcome(await enrollments.ChangeModeAsync(id, mode, Aborted), "The study mode was changed.", returnUrl);

    [HttpGet]
    public async Task<IActionResult> Create(string? q)
        => View(new ManualEnrollmentPage(new ManualEnrollmentInput(), await users.SearchStudentsAsync(q, Aborted), await courses.ListAsync(Aborted)));

    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ManualEnrollmentInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await enrollments.EnrollManuallyAsync(input, Aborted);
            if (result.Succeeded)
            {
                Flash("The student was enrolled.");
                return RedirectToAction(nameof(Index));
            }

            TryAddFormError(result.Error!);
        }

        return View(new ManualEnrollmentPage(input, await users.SearchStudentsAsync(null, Aborted), await courses.ListAsync(Aborted)));
    }

    private IActionResult Outcome(Result result, string success, string? returnUrl)
    {
        if (result.Failed)
        {
            if (result.Error!.Kind == ErrorKind.NotFound)
            {
                return NotFound();
            }

            Flash(result.Error.Message, Infrastructure.Mvc.FlashKind.Error);
        }
        else
        {
            Flash(success);
        }

        return LocalRedirectOr(returnUrl, nameof(Index), "Enrollments");
    }
}

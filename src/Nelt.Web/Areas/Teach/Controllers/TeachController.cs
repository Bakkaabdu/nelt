using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelt.Domain.Enums;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Teach.Controllers;

/// <summary>Instructor workspace. Admins can use it for every course; instructors only for their own (enforced by the services).</summary>
[Area("Teach")]
[Authorize(Roles = Roles.Staff)]
public abstract class TeachController : AppController;

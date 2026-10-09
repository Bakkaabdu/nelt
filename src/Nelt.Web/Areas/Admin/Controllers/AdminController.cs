using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelt.Domain.Enums;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public abstract class AdminController : AppController;

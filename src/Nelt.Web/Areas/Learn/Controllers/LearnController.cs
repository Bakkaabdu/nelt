using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nelt.Domain.Enums;
using Nelt.Web.Infrastructure.Mvc;

namespace Nelt.Web.Areas.Learn.Controllers;

[Area("Learn")]
[Authorize(Roles = Roles.Student)]
public abstract class LearnController : AppController;

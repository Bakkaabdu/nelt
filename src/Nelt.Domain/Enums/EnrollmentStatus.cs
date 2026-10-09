using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

public enum EnrollmentStatus
{
    [Display(Name = "Awaiting payment")] Pending = 1,
    [Display(Name = "Active")] Active = 2,
    [Display(Name = "Completed")] Completed = 3,
    [Display(Name = "Cancelled")] Cancelled = 4,
}

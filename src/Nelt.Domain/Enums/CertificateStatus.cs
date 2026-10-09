using System.ComponentModel.DataAnnotations;

namespace Nelt.Domain.Enums;

public enum CertificateStatus
{
    [Display(Name = "Under review")] Pending = 1,
    [Display(Name = "Issued")] Approved = 2,
    [Display(Name = "Declined")] Rejected = 3,
}

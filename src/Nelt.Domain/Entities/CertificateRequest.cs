using Nelt.Domain.Common;
using Nelt.Domain.Enums;

namespace Nelt.Domain.Entities;

public class CertificateRequest : Entity
{
    public int EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }

    public CertificateStatus Status { get; set; } = CertificateStatus.Pending;
    public DateTime RequestedAt { get; set; }

    /// <summary>Snapshot of the evaluation at request/approval time, so issued certificates never change.</summary>
    public decimal FinalScore { get; set; }
    public bool EligibleForNextLevel { get; set; }

    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public string? ReviewNote { get; set; }

    /// <summary>Public verification number, assigned when the certificate is issued.</summary>
    public string? SerialNumber { get; set; }
    public DateTime? IssuedAt { get; set; }
}

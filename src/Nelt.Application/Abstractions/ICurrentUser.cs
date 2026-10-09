using Nelt.Domain.Enums;

namespace Nelt.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool IsAdmin => IsInRole(Roles.Admin);
    bool IsStaff => IsInRole(Roles.Admin) || IsInRole(Roles.Instructor);

    /// <summary>Returns the id or throws when used from an anonymous context (a programming error, not a user error).</summary>
    Guid RequiredUserId => UserId ?? throw new InvalidOperationException("An authenticated user is required.");
}

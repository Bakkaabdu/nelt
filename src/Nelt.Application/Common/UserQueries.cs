using Nelt.Application.Abstractions;

namespace Nelt.Application.Common;

internal static class UserQueries
{
    /// <summary>Composable sub-query of the ids of users holding any of the given roles.</summary>
    public static IQueryable<Guid> UserIdsInRoles(this IAppDbContext db, params string[] roles)
        => db.UserRoles
            .Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && roles.Contains(r.Name!)))
            .Select(ur => ur.UserId);
}

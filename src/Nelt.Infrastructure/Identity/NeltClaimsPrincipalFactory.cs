using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nelt.Domain.Entities;

namespace Nelt.Infrastructure.Identity;

public static class NeltClaims
{
    public const string FullName = "nelt:full_name";
}

/// <summary>Adds the display name to the auth cookie so layouts never need a database round-trip.</summary>
public sealed class NeltClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(NeltClaims.FullName, user.FullName));
        return identity;
    }
}

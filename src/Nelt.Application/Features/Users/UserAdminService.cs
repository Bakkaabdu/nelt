using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nelt.Application.Abstractions;
using Nelt.Application.Common;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;

namespace Nelt.Application.Features.Users;

public sealed record UserRow(Guid Id, string FullName, string Email, string? Phone, string Role, string? BiometricId, bool IsActive, DateTime CreatedAt, int Enrollments);

public sealed record UserFilter(string? Role = null, string? Search = null, int Page = 1);

public sealed class UserInput : IValidatableObject
{
    [Required, StringLength(120), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(160), Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(40), Display(Name = "Phone")]
    public string? PhoneNumber { get; set; }

    [Required, Display(Name = "Role")]
    public string Role { get; set; } = Roles.Student;

    [StringLength(32), RegularExpression("^[0-9A-Za-z]+$", ErrorMessage = "Use the numeric ID shown on the fingerprint device."), Display(Name = "Fingerprint ID")]
    public string? BiometricId { get; set; }

    [Display(Name = "Account active")]
    public bool IsActive { get; set; } = true;

    [StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "Password")]
    public string? Password { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Roles.All.Contains(Role))
        {
            yield return new ValidationResult("Unknown role.", [nameof(Role)]);
        }
    }
}

public sealed record UserEditModel(Guid? Id, UserInput Input, bool IsSelf);

public sealed record StudentOption(Guid Id, string Name, string Email);

public interface IUserAdminService
{
    Task<PagedList<UserRow>> ListAsync(UserFilter filter, CancellationToken ct = default);
    Task<UserEditModel?> GetForEditAsync(Guid id, CancellationToken ct = default);
    Task<Result<Guid>> CreateAsync(UserInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(Guid id, UserInput input, CancellationToken ct = default);
    Task<IReadOnlyList<StudentOption>> SearchStudentsAsync(string? term, CancellationToken ct = default);
}

internal sealed class UserAdminService(IAppDbContext db, UserManager<ApplicationUser> users, ICurrentUser current, IPlatformTime time) : IUserAdminService
{
    public Task<PagedList<UserRow>> ListAsync(UserFilter filter, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Role) && Roles.All.Contains(filter.Role))
        {
            var ids = db.UserIdsInRoles(filter.Role);
            query = query.Where(u => ids.Contains(u.Id));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(u => u.FullName.Contains(term) || u.Email!.Contains(term) || u.PhoneNumber!.Contains(term) || u.BiometricId == term);
        }

        return query.OrderBy(u => u.FullName)
            .Select(u => new UserRow(
                u.Id, u.FullName, u.Email ?? string.Empty, u.PhoneNumber,
                db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).FirstOrDefault() ?? Roles.Student,
                u.BiometricId, u.IsActive, u.CreatedAt,
                db.Enrollments.Count(e => e.StudentId == u.Id)))
            .ToPagedListAsync(filter.Page, ct: ct);
    }

    public async Task<UserEditModel?> GetForEditAsync(Guid id, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = await users.GetRolesAsync(user);
        var input = new UserInput
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            Role = roles.FirstOrDefault() ?? Roles.Student,
            BiometricId = user.BiometricId,
            IsActive = user.IsActive,
        };
        return new UserEditModel(user.Id, input, user.Id == current.UserId);
    }

    public async Task<Result<Guid>> CreateAsync(UserInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Password))
        {
            return Error.Validation("A password is required for new accounts.", nameof(UserInput.Password));
        }

        if (await BiometricConflictAsync(null, input.BiometricId, ct))
        {
            return Error.Validation("This fingerprint ID is already assigned to another person.", nameof(UserInput.BiometricId));
        }

        var user = new ApplicationUser
        {
            UserName = input.Email.Trim(),
            Email = input.Email.Trim(),
            EmailConfirmed = true,
            FullName = input.FullName.Trim(),
            PhoneNumber = Clean(input.PhoneNumber),
            BiometricId = Clean(input.BiometricId),
            IsActive = input.IsActive,
            CreatedAt = time.UtcNow,
        };

        var created = await users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
        {
            return IdentityError(created);
        }

        var role = await users.AddToRoleAsync(user, input.Role);
        if (!role.Succeeded)
        {
            return IdentityError(role);
        }

        await ApplyActiveStateAsync(user, input.IsActive);
        return user.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, UserInput input, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Error.NotFound();
        }

        var isSelf = user.Id == current.UserId;
        var currentRoles = await users.GetRolesAsync(user);
        if (isSelf && (!input.IsActive || !input.Role.Equals(Roles.Admin, StringComparison.Ordinal)) && currentRoles.Contains(Roles.Admin))
        {
            return Error.Validation("You cannot remove your own administrator access.", nameof(UserInput.Role));
        }

        if (await BiometricConflictAsync(id, input.BiometricId, ct))
        {
            return Error.Validation("This fingerprint ID is already assigned to another person.", nameof(UserInput.BiometricId));
        }

        user.FullName = input.FullName.Trim();
        user.PhoneNumber = Clean(input.PhoneNumber);
        user.BiometricId = Clean(input.BiometricId);
        user.IsActive = input.IsActive;

        var email = input.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var emailResult = await users.SetEmailAsync(user, email);
            if (!emailResult.Succeeded)
            {
                return IdentityError(emailResult);
            }

            await users.SetUserNameAsync(user, email);
            user.EmailConfirmed = true;
        }

        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return IdentityError(updated);
        }

        if (!currentRoles.SequenceEqual([input.Role]))
        {
            await users.RemoveFromRolesAsync(user, currentRoles);
            var role = await users.AddToRoleAsync(user, input.Role);
            if (!role.Succeeded)
            {
                return IdentityError(role);
            }
        }

        await ApplyActiveStateAsync(user, input.IsActive);

        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, input.Password);
            if (!reset.Succeeded)
            {
                return IdentityError(reset);
            }
        }

        return Result.Success();
    }

    public async Task<IReadOnlyList<StudentOption>> SearchStudentsAsync(string? term, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking().Where(u => u.IsActive && db.UserIdsInRoles(Roles.Student).Contains(u.Id));
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(u => u.FullName.Contains(t) || u.Email!.Contains(t) || u.PhoneNumber!.Contains(t));
        }

        return await query.OrderBy(u => u.FullName).Take(500)
            .Select(u => new StudentOption(u.Id, u.FullName, u.Email ?? string.Empty)).ToListAsync(ct);
    }

    /// <summary>Inactive accounts are locked out indefinitely and their existing sessions are invalidated.</summary>
    private async Task ApplyActiveStateAsync(ApplicationUser user, bool isActive)
    {
        if (isActive)
        {
            if (await users.IsLockedOutAsync(user))
            {
                await users.SetLockoutEndDateAsync(user, null);
            }

            return;
        }

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);
    }

    private Task<bool> BiometricConflictAsync(Guid? id, string? biometricId, CancellationToken ct)
    {
        var value = Clean(biometricId);
        return value is null ? Task.FromResult(false) : db.Users.AnyAsync(u => u.BiometricId == value && u.Id != id, ct);
    }

    private static Error IdentityError(IdentityResult result)
        => Error.Validation(result.Errors.FirstOrDefault()?.Description ?? "The account could not be saved.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

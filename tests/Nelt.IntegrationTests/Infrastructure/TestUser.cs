using Microsoft.AspNetCore.Http;
using Nelt.Application.Abstractions;
using Nelt.Web.Infrastructure;

namespace Nelt.IntegrationTests.Infrastructure;

/// <summary>
/// Lets service-level tests act as a specific user without an HTTP request:
/// <c>using var _ = TestUser.As(id, Roles.Instructor);</c> then resolve services from a new scope.
/// </summary>
public static class TestUser
{
    private static readonly AsyncLocal<Identity?> Current = new();

    internal static Identity? Value => Current.Value;

    public static IDisposable As(Guid userId, params string[] roles)
    {
        var previous = Current.Value;
        Current.Value = new Identity(userId, roles);
        return new Restore(previous);
    }

    /// <summary>Runs the following code as an anonymous visitor.</summary>
    public static IDisposable Anonymous()
    {
        var previous = Current.Value;
        Current.Value = new Identity(null, []);
        return new Restore(previous);
    }

    internal sealed record Identity(Guid? UserId, IReadOnlyCollection<string> Roles);

    private sealed class Restore(Identity? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

/// <summary>Uses the <see cref="TestUser"/> identity when one is set, otherwise the real signed-in HTTP user.</summary>
public sealed class TestCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private readonly CurrentUser _http = new(accessor);

    public Guid? UserId => TestUser.Value is { } test ? test.UserId : _http.UserId;

    public bool IsAuthenticated => TestUser.Value is { } test ? test.UserId is not null : _http.IsAuthenticated;

    public bool IsInRole(string role) => TestUser.Value is { } test ? test.Roles.Contains(role) : _http.IsInRole(role);
}

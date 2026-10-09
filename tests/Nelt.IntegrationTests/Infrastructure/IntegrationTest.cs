using System.Net;
using Nelt.Application.Common;

namespace Nelt.IntegrationTests.Infrastructure;

/// <summary>Base class: every test class shares one running application and database.</summary>
[Collection(NeltCollection.Name)]
public abstract class IntegrationTest(NeltFixture fixture)
{
    protected NeltFixture Fixture { get; } = fixture;

    protected SeedData Seed => Fixture.Seed;

    /// <summary>GETs each path and returns one readable line (plus the exception text) per page with an unexpected status.</summary>
    protected static async Task<List<string>> GetAllAsync(HttpClient client, IEnumerable<string> paths, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var failures = new List<string>();
        foreach (var path in paths)
        {
            using var response = await client.GetAsync(path);
            if (response.StatusCode != expected)
            {
                var body = await response.Content.ReadAsStringAsync();
                failures.Add($"GET {path} -> {(int)response.StatusCode} {response.StatusCode} (expected {(int)expected}). Location: {response.Headers.Location}\n    {Html.Excerpt(body)}");
            }
        }

        return failures;
    }

    protected static void AssertNoFailures(IReadOnlyCollection<string> failures)
        => Assert.True(failures.Count == 0, $"{failures.Count} page(s) failed:\n\n{string.Join("\n\n", failures)}");

    /// <summary>Forbidden pages either return 403 or redirect to the access-denied page (cookie authentication).</summary>
    protected static bool IsDenied(HttpResponseMessage response)
        => response.StatusCode == HttpStatusCode.Forbidden
           || (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("access-denied", StringComparison.OrdinalIgnoreCase) == true);

    protected static bool IsLoginRedirect(HttpResponseMessage response)
        => response.StatusCode == HttpStatusCode.Redirect
           && response.Headers.Location?.OriginalString.Contains("/account/login", StringComparison.OrdinalIgnoreCase) == true;

    protected static T Ok<T>(Result<T> result)
    {
        Assert.True(result.Succeeded, $"Expected success but got {result.Error?.Kind}: {result.Error?.Message} (field: {result.Error?.Field})");
        return result.Value;
    }

    protected static void Ok(Result result)
        => Assert.True(result.Succeeded, $"Expected success but got {result.Error?.Kind}: {result.Error?.Message} (field: {result.Error?.Field})");

    protected static Error Fails(Result result, ErrorKind kind)
    {
        Assert.True(result.Failed, $"Expected a {kind} error but the call succeeded.");
        Assert.Equal(kind, result.Error!.Kind);
        return result.Error;
    }
}

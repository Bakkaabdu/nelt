using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nelt.Domain.Entities;
using Nelt.Domain.Enums;
using Nelt.Infrastructure.Persistence;

namespace Nelt.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class NeltCollection : ICollectionFixture<NeltFixture>
{
    public const string Name = "Nelt application";
}

/// <summary>
/// One application instance and one throw-away SQL Server database for the whole test run.
/// The database is named <c>NeltTests_&lt;guid&gt;</c> and dropped at the end, so it never touches real data.
/// </summary>
public sealed class NeltFixture : IAsyncLifetime
{
    public const string Password = "Test-Passw0rd!";
    public const string AdminEmail = "admin@nelt.test";

    /// <summary>
    /// Server part of the connection string (no database). Override with the NELT_TEST_SQL environment variable.
    /// The default matches the SQL Server container from docker-compose.yml (docker compose up -d db).
    /// </summary>
    private const string DefaultServer = "Server=localhost,1433;User Id=sa;Password=Nelt_Local_2026!;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private static readonly Regex AntiforgeryPattern = new("name=\"__RequestVerificationToken\"[^>]*?value=\"([^\"]+)\"", RegexOptions.Compiled);

    private readonly string _databaseName = $"NeltTests_{Guid.NewGuid():N}";
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "nelt-tests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _signInLock = new(1, 1);
    private int _userCounter;
    private int _clientCounter;

    public NeltFactory Factory { get; private set; } = null!;

    public SeedData Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("NELT_TEST_SQL");
        if (string.IsNullOrWhiteSpace(server))
        {
            server = DefaultServer;
        }

        // Environment variables are read by WebApplication.CreateBuilder before Program configures services,
        // and they override appsettings.Development.json and user secrets.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"{server.TrimEnd(';')};Database={_databaseName}");
        Environment.SetEnvironmentVariable("Seed__AdminEmail", AdminEmail);
        Environment.SetEnvironmentVariable("Seed__AdminPassword", Password);
        Environment.SetEnvironmentVariable("Seed__AdminName", "Test Administrator");
        Environment.SetEnvironmentVariable("Storage__RootPath", _storageRoot);
        Environment.SetEnvironmentVariable("Devices__AllowPlainHttp", "false");

        Factory = new NeltFactory();
        _ = Factory.Server; // Starts the app: creates the schema, roles, levels, settings and the administrator.

        Seed = await SeedData.CreateAsync(Factory.Services);
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        if (Factory is not null)
        {
            try
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            }
            finally
            {
                await Factory.DisposeAsync();
            }
        }

        _signInLock.Dispose();
        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp files are harmless.
        }
    }

    /// <summary>A fresh DI scope (one DbContext), like one HTTP request.</summary>
    public AsyncServiceScope Scope() => Factory.Services.CreateAsyncScope();

    /// <summary>An HTTPS client with its own cookie jar that does not follow redirects (so tests can assert them).</summary>
    public HttpClient CreateClient()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

        // Each client looks like a different machine, so the per-IP sign-in rate limit never trips during the run.
        var number = Interlocked.Increment(ref _clientCounter);
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.Header, $"10.{(number >> 16) & 255}.{(number >> 8) & 255}.{number & 255}");
        return client;
    }

    /// <summary>
    /// A signed-in client for the given account, created once and reused.
    /// (Sign-in is rate limited to 10 attempts per minute, so clients are cached.)
    /// </summary>
    public async Task<HttpClient> ClientForAsync(string email)
    {
        await _signInLock.WaitAsync();
        try
        {
            if (_clients.TryGetValue(email, out var cached))
            {
                return cached;
            }

            var client = CreateClient();
            using var response = await SignInAsync(client, email, Password);
            if (response.StatusCode != HttpStatusCode.Redirect)
            {
                var body = await response.Content.ReadAsStringAsync();
                client.Dispose();
                throw new InvalidOperationException($"Sign-in failed for {email}: HTTP {(int)response.StatusCode}\n{Html.Excerpt(body)}");
            }

            _clients[email] = client;
            return client;
        }
        finally
        {
            _signInLock.Release();
        }
    }

    public static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string email, string password)
    {
        var token = await AntiforgeryTokenAsync(client, "/account/login");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = token,
        });
        return await client.PostAsync("/account/login", form);
    }

    /// <summary>Loads a page and returns the antiforgery token rendered in its forms (the cookie half stays in the client).</summary>
    public static async Task<string> AntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET {path} returned HTTP {(int)response.StatusCode}\n{Html.Excerpt(html)}");
        }

        var match = AntiforgeryPattern.Match(html);
        return match.Success
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException($"No antiforgery token found on {path}.");
    }

    /// <summary>Creates an extra active account with a unique email (for tests that need a clean user).</summary>
    public async Task<ApplicationUser> CreateUserAsync(string role, string? biometricId = null)
    {
        var number = Interlocked.Increment(ref _userCounter);
        await using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await SeedData.CreateUserAsync(users, $"{role.ToLowerInvariant()}{number}@extra.nelt.test", $"Extra {role} {number}", role, biometricId);
    }

    /// <summary>Convenience for tests that only need a student id.</summary>
    public async Task<Guid> CreateStudentAsync() => (await CreateUserAsync(Roles.Student)).Id;
}

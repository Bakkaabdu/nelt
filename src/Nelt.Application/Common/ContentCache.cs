using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Nelt.Application.Common;

/// <summary>
/// Short-lived cache for public, rarely-changing content (landing page, settings, catalog).
/// Any admin change calls <see cref="Invalidate"/>, which evicts every entry at once through a shared change token.
/// On multiple instances, other nodes converge within the TTL.
/// </summary>
public sealed class ContentCache(IMemoryCache cache) : IDisposable
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private CancellationTokenSource _version = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct, TimeSpan? ttl = null)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var token = new CancellationChangeToken(_version.Token);
        var value = await factory(ct);

        using (var entry = cache.CreateEntry(key))
        {
            entry.Value = value;
            entry.AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl;
            entry.AddExpirationToken(token);
        }

        return value;
    }

    public void Invalidate()
    {
        var previous = Interlocked.Exchange(ref _version, new CancellationTokenSource());
        // Not disposed on purpose: a concurrent reader may still be taking the token of the old source.
        previous.Cancel();
    }

    public void Dispose() => _version.Dispose();
}

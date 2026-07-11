using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagBook.Modules.Settings;
using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Settings;

/// <summary>
/// <see cref="IApiKeyStore"/> over <see cref="IMemoryCache"/> — the BYOK key lives in process memory
/// only, never the database (constitution §VII). Entries are keyed by the current session and expire
/// after <see cref="ApiKeyStoreOptions.Ttl"/>. An explicit <see cref="TimeProvider"/> expiry is kept
/// alongside the cache entry so TTL is deterministically testable and honored even if the cache clock
/// differs; the cache's own absolute expiration is the memory backstop.
/// </summary>
public sealed class MemoryCacheApiKeyStore(
    IMemoryCache cache,
    ISessionContext session,
    TimeProvider timeProvider,
    IOptions<ApiKeyStoreOptions> options)
    : IApiKeyStore
{
    private sealed record Entry(string ApiKey, DateTimeOffset ExpiresAt);

    private string CacheKey => $"apikey:{session.UserSessionId}";

    /// <inheritdoc />
    public string? Get()
    {
        if (!cache.TryGetValue(CacheKey, out Entry? entry) || entry is null)
        {
            return null;
        }

        if (timeProvider.GetUtcNow() >= entry.ExpiresAt)
        {
            cache.Remove(CacheKey);

            return null;
        }

        return entry.ApiKey;
    }

    /// <inheritdoc />
    public void Set(string apiKey)
    {
        TimeSpan ttl = options.Value.Ttl;
        DateTimeOffset expiresAt = timeProvider.GetUtcNow().Add(ttl);

        // The explicit ExpiresAt (via TimeProvider) is the authoritative, testable TTL; the cache's own
        // relative expiration is a wall-clock backstop that evicts the entry to reclaim memory.
        cache.Set(CacheKey, new Entry(apiKey, expiresAt), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
        });
    }

    /// <inheritdoc />
    public void Remove()
    {
        cache.Remove(CacheKey);
    }
}

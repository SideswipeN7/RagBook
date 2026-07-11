using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagBook.Modules.Settings;
using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Settings;

/// <summary>
/// <see cref="IApiKeyThrottle"/> as a per-session fixed-window counter over <see cref="IMemoryCache"/>
/// (US-02 FR-004b). The window opens on the first attempt and its end is fixed for the window's life,
/// so repeated attempts do not slide it. The stored counter is a reference type mutated in place, so
/// increments persist without resetting the entry's expiration.
/// </summary>
public sealed class MemoryCacheApiKeyThrottle(
    IMemoryCache cache,
    ISessionContext session,
    TimeProvider timeProvider,
    IOptions<ApiKeyStoreOptions> options)
    : IApiKeyThrottle
{
    private sealed class Window
    {
        public int Count { get; set; }
    }

    private string CacheKey => $"apikey-attempts:{session.UserSessionId}";

    /// <inheritdoc />
    public bool TryRegisterAttempt()
    {
        ApiKeyStoreOptions settings = options.Value;

        if (!cache.TryGetValue(CacheKey, out Window? window) || window is null)
        {
            DateTimeOffset windowEnd = timeProvider.GetUtcNow().Add(settings.ThrottleWindow);
            cache.Set(CacheKey, new Window { Count = 1 }, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = windowEnd,
            });

            return true;
        }

        if (window.Count >= settings.ThrottleMaxAttempts)
        {
            return false;
        }

        window.Count++;

        return true;
    }
}

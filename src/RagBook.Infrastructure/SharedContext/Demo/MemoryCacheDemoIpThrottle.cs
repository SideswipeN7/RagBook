using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagBook.Modules.Demo;
using RagBook.Modules.Demo.Domain;

namespace RagBook.Infrastructure.SharedContext.Demo;

/// <summary>
/// <see cref="IDemoIpThrottle"/> as a per-IP fixed hourly-window counter over <see cref="IMemoryCache"/> (US-03
/// AC-3). The window opens on the first request from an IP and its end is fixed for the window's life (repeated
/// requests do not slide it); once the hourly limit is hit, subsequent requests are denied with the seconds
/// remaining to the window reset for the <c>Retry-After</c> header. Time comes from <see cref="TimeProvider"/>.
/// </summary>
public sealed class MemoryCacheDemoIpThrottle(
    IMemoryCache cache,
    TimeProvider timeProvider,
    IOptions<DemoOptions> options)
    : IDemoIpThrottle
{
    private sealed class Window
    {
        public int Count { get; set; }

        public DateTimeOffset EndsAt { get; init; }
    }

    /// <inheritdoc />
    public (bool Allowed, int RetryAfterSeconds) TryRegister(string ipAddress)
    {
        DemoOptions settings = options.Value;
        string cacheKey = $"demo-ip:{ipAddress}";
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Open a fresh window when there is none or the current one has elapsed. The window end is tracked against
        // the injected clock (so the limit is deterministic); the cache entry gets a real-clock lifetime as a
        // safety net so it survives between requests regardless of the TimeProvider.
        if (!cache.TryGetValue(cacheKey, out Window? window) || window is null || now >= window.EndsAt)
        {
            cache.Set(cacheKey, new Window { Count = 1, EndsAt = now.AddHours(1) }, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            });

            return (true, 0);
        }

        if (window.Count >= settings.MaxQuestionsPerIpPerHour)
        {
            int retryAfter = Math.Max(1, (int)Math.Ceiling((window.EndsAt - now).TotalSeconds));

            return (false, retryAfter);
        }

        window.Count++;

        return (true, 0);
    }
}

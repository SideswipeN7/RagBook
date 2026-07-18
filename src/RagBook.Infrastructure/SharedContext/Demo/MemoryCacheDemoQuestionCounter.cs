using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagBook.Modules.Demo;
using RagBook.Modules.Demo.Domain;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Demo;

/// <summary>
/// <see cref="IDemoQuestionCounter"/> as a per-session lifetime counter over <see cref="IMemoryCache"/> (US-03
/// AC-2), mirroring the US-02 throttle pattern. Keyed by the ambient session; the entry uses a sliding expiration
/// (<see cref="DemoOptions.SessionCounterTtlHours"/>) so it stays alive for the session's active life rather than a
/// short rate window (the per-IP hourly limit is the rate control). The stored counter is a reference type mutated
/// in place, so increments persist without resetting the entry.
/// </summary>
public sealed class MemoryCacheDemoQuestionCounter(
    IMemoryCache cache,
    ISessionContext session,
    IOptions<DemoOptions> options)
    : IDemoQuestionCounter
{
    private sealed class Counter
    {
        public int Count { get; set; }
    }

    private string CacheKey => $"demo-questions:{session.UserSessionId}";

    /// <inheritdoc />
    public int Asked()
    {
        return cache.TryGetValue(CacheKey, out Counter? counter) && counter is not null ? counter.Count : 0;
    }

    /// <inheritdoc />
    public int Remaining()
    {
        return Math.Max(0, options.Value.MaxQuestionsPerSession - Asked());
    }

    /// <inheritdoc />
    public bool TryConsume()
    {
        DemoOptions settings = options.Value;

        if (!cache.TryGetValue(CacheKey, out Counter? counter) || counter is null)
        {
            counter = new Counter();
            cache.Set(CacheKey, counter, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(Math.Max(1, settings.SessionCounterTtlHours)),
            });
        }

        if (counter.Count >= settings.MaxQuestionsPerSession)
        {
            return false;
        }

        counter.Count++;

        return true;
    }
}

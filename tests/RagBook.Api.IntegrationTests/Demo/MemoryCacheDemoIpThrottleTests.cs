using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagBook.Api.IntegrationTests.Settings.Fakes;
using RagBook.Infrastructure.SharedContext.Demo;
using RagBook.Modules.Demo;
using Xunit;

namespace RagBook.Api.IntegrationTests.Demo;

/// <summary>
/// Unit tests for <see cref="MemoryCacheDemoIpThrottle"/> (US-03 AC-3): a per-IP fixed hourly window that admits up
/// to the cap then denies with a positive <c>Retry-After</c>, and isolates counts per IP. Deterministic via a
/// settable <see cref="TimeProvider"/>. No container.
/// </summary>
public sealed class MemoryCacheDemoIpThrottleTests
{
    private static MemoryCacheDemoIpThrottle CreateThrottle(TimeProvider clock, int perHour)
    {
        var options = Options.Create(new DemoOptions { MaxQuestionsPerIpPerHour = perHour });

        return new MemoryCacheDemoIpThrottle(new MemoryCache(new MemoryCacheOptions()), clock, options);
    }

    [Fact]
    public void Should_AllowUpToTheCap_ThenDenyWithRetryAfter()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UnixEpoch);
        MemoryCacheDemoIpThrottle throttle = CreateThrottle(clock, perHour: 2);

        throttle.TryRegister("203.0.113.7").Allowed.Should().BeTrue();
        throttle.TryRegister("203.0.113.7").Allowed.Should().BeTrue();

        (bool allowed, int retryAfter) = throttle.TryRegister("203.0.113.7");
        allowed.Should().BeFalse();
        retryAfter.Should().BePositive().And.BeLessThanOrEqualTo(3600);
    }

    [Fact]
    public void Should_IsolatePerIp()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UnixEpoch);
        MemoryCacheDemoIpThrottle throttle = CreateThrottle(clock, perHour: 1);

        throttle.TryRegister("198.51.100.1").Allowed.Should().BeTrue();

        // A different IP still has its own allowance.
        throttle.TryRegister("198.51.100.2").Allowed.Should().BeTrue();
    }
}

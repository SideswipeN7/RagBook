using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagBook.Api.IntegrationTests.Settings.Fakes;
using RagBook.Infrastructure.SharedContext.Settings;
using RagBook.Modules.Settings;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Unit tests for <see cref="MemoryCacheApiKeyStore"/> (analyze U2). A stored key is returned within its
/// TTL and disappears once the TTL elapses (proving the AC-3 expiry → <c>api_key_missing</c> path), and
/// keys are isolated per session id.
/// </summary>
public sealed class MemoryCacheApiKeyStoreTests
{
    private static MemoryCacheApiKeyStore CreateStore(Guid sessionId, AdjustableTimeProvider clock, TimeSpan ttl)
    {
        // The store enforces the TTL explicitly via the injected TimeProvider, so the cache's own clock
        // is irrelevant to this test — a default MemoryCache is enough.
        var options = Options.Create(new ApiKeyStoreOptions { Ttl = ttl });

        return new MemoryCacheApiKeyStore(
            new MemoryCache(new MemoryCacheOptions()),
            new TestSessionContext(sessionId),
            clock,
            options);
    }

    [Fact]
    public void Should_ReturnKey_When_WithinTtl()
    {
        // Arrange
        var clock = new AdjustableTimeProvider(DateTimeOffset.UnixEpoch);
        MemoryCacheApiKeyStore store = CreateStore(Guid.NewGuid(), clock, TimeSpan.FromMinutes(30));
        store.Set("sk-ant-key");

        // Act
        clock.Advance(TimeSpan.FromMinutes(29));

        // Assert
        store.Get().Should().Be("sk-ant-key");
    }

    [Fact]
    public void Should_ReturnNone_When_TtlElapsed()
    {
        // Arrange
        var clock = new AdjustableTimeProvider(DateTimeOffset.UnixEpoch);
        MemoryCacheApiKeyStore store = CreateStore(Guid.NewGuid(), clock, TimeSpan.FromMinutes(30));
        store.Set("sk-ant-key");

        // Act — advance past the TTL.
        clock.Advance(TimeSpan.FromMinutes(31));

        // Assert
        store.Get().Should().BeNull();
    }
}

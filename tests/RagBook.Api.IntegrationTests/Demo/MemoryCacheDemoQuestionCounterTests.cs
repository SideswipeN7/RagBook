using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagBook.Api.IntegrationTests.Settings.Fakes;
using RagBook.Infrastructure.SharedContext.Demo;
using RagBook.Modules.Demo;
using Xunit;

namespace RagBook.Api.IntegrationTests.Demo;

/// <summary>
/// Unit tests for <see cref="MemoryCacheDemoQuestionCounter"/> (US-03 AC-2): a per-session lifetime counter that
/// admits up to the configured maximum, decrements the reported remaining, and refuses past the cap. No container.
/// </summary>
public sealed class MemoryCacheDemoQuestionCounterTests
{
    private static MemoryCacheDemoQuestionCounter CreateCounter(Guid sessionId, int max)
    {
        var options = Options.Create(new DemoOptions { MaxQuestionsPerSession = max });

        return new MemoryCacheDemoQuestionCounter(
            new MemoryCache(new MemoryCacheOptions()),
            new TestSessionContext(sessionId),
            options);
    }

    [Fact]
    public void Should_ConsumeUpToTheCap_ThenRefuse()
    {
        MemoryCacheDemoQuestionCounter counter = CreateCounter(Guid.NewGuid(), max: 3);

        counter.TryConsume().Should().BeTrue();
        counter.TryConsume().Should().BeTrue();
        counter.TryConsume().Should().BeTrue();

        counter.TryConsume().Should().BeFalse(); // 4th over the cap of 3
        counter.Asked().Should().Be(3);
        counter.Remaining().Should().Be(0);
    }

    [Fact]
    public void Should_ReportRemainingAsItDecrements()
    {
        MemoryCacheDemoQuestionCounter counter = CreateCounter(Guid.NewGuid(), max: 2);

        counter.Remaining().Should().Be(2);
        counter.TryConsume();
        counter.Remaining().Should().Be(1);
        counter.TryConsume();
        counter.Remaining().Should().Be(0);
    }

    [Fact]
    public void Should_IsolateCountsPerSession()
    {
        MemoryCacheDemoQuestionCounter a = CreateCounter(Guid.NewGuid(), max: 1);
        MemoryCacheDemoQuestionCounter b = CreateCounter(Guid.NewGuid(), max: 1);

        a.TryConsume().Should().BeTrue();

        // A different session still has its full allowance.
        b.Remaining().Should().Be(1);
        b.TryConsume().Should().BeTrue();
    }
}

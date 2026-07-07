namespace RagBook.Api.IntegrationTests.Testing;

/// <summary>A <see cref="TimeProvider"/> that always returns a fixed instant, for deterministic tests.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}

namespace RagBook.Api.IntegrationTests.Settings.Fakes;

/// <summary>A <see cref="TimeProvider"/> whose current time is settable, for deterministic TTL tests.</summary>
public sealed class AdjustableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }

    /// <summary>Advances the current time by <paramref name="delta"/>.</summary>
    public void Advance(TimeSpan delta)
    {
        _now = _now.Add(delta);
    }
}

using RagBook.Modules.Settings.Domain;

namespace RagBook.Api.IntegrationTests.Settings.Fakes;

/// <summary>Trivial in-memory <see cref="IApiKeyStore"/> for directly testing the client factory guard.</summary>
public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private string? _apiKey;

    /// <inheritdoc />
    public string? Get()
    {
        return _apiKey;
    }

    /// <inheritdoc />
    public void Set(string apiKey)
    {
        _apiKey = apiKey;
    }

    /// <inheritdoc />
    public void Remove()
    {
        _apiKey = null;
    }
}

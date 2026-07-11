using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Shared.Results;

namespace RagBook.Infrastructure.SharedContext.Providers.Anthropic;

/// <summary>
/// <see cref="IAnthropicClientFactory"/> that resolves the current session's key from the store. With
/// no active key it fails <see cref="SettingsErrors.ApiKeyMissing"/> (US-02 AC-3, FR-009) — the guard
/// future chat (US-14) relies on. US-14 will expand the returned handle into the real streaming client.
/// </summary>
public sealed class AnthropicClientFactory(IApiKeyStore store) : IAnthropicClientFactory
{
    /// <inheritdoc />
    public Result<AnthropicClientHandle> CreateForSession()
    {
        string? apiKey = store.Get();

        return apiKey is null
            ? Result.Failure<AnthropicClientHandle>(SettingsErrors.ApiKeyMissing)
            : new AnthropicClientHandle(apiKey);
    }
}

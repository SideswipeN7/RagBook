using Microsoft.Extensions.Options;
using RagBook.Modules.Demo.Errors;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Shared.Results;

namespace RagBook.Infrastructure.SharedContext.Providers.Anthropic;

/// <summary>
/// <see cref="IAnthropicClientFactory"/> that resolves the current session's BYOK key from the store, or — for
/// demo answers (US-03) — the server-held application key from <see cref="AnthropicOptions.ApplicationKey"/>. With
/// no active session key it fails <see cref="SettingsErrors.ApiKeyMissing"/> (US-02 AC-3); with no application key
/// it fails <see cref="DemoErrors.Unavailable"/> (US-03). The returned handle carries only the key string.
/// </summary>
public sealed class AnthropicClientFactory(IApiKeyStore store, IOptions<AnthropicOptions> options)
    : IAnthropicClientFactory
{
    /// <inheritdoc />
    public Result<AnthropicClientHandle> CreateForSession()
    {
        string? apiKey = store.Get();

        return apiKey is null
            ? Result.Failure<AnthropicClientHandle>(SettingsErrors.ApiKeyMissing)
            : new AnthropicClientHandle(apiKey);
    }

    /// <inheritdoc />
    public Result<AnthropicClientHandle> CreateForDemo()
    {
        string? applicationKey = options.Value.ApplicationKey;

        return string.IsNullOrWhiteSpace(applicationKey)
            ? Result.Failure<AnthropicClientHandle>(DemoErrors.Unavailable)
            : new AnthropicClientHandle(applicationKey);
    }
}

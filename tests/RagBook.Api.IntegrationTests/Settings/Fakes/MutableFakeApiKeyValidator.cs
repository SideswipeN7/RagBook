using RagBook.Modules.Settings.Domain;

namespace RagBook.Api.IntegrationTests.Settings.Fakes;

/// <summary>
/// In-memory <see cref="IApiKeyValidator"/> so no integration test hits Anthropic (constitution §V).
/// The next outcome is settable per test; the call count lets a test assert the provider was (not) reached.
/// </summary>
public sealed class MutableFakeApiKeyValidator : IApiKeyValidator
{
    /// <summary>The outcome the next validation returns.</summary>
    public ApiKeyValidationResult NextResult { get; set; } = ApiKeyValidationResult.Valid;

    /// <summary>How many times <see cref="ValidateAsync"/> has been invoked.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult(NextResult);
    }
}

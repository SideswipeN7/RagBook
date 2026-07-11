using RagBook.Modules.Settings.Domain;

namespace RagBook.Modules.Settings.Features.GetApiKeyStatus;

/// <summary>
/// Handles <see cref="GetApiKeyStatusQuery"/>. Projects the session's stored key to <c>none</c> or
/// <c>active</c> + mask — never the full key (US-02 AC-2, FR-008).
/// </summary>
public sealed class GetApiKeyStatusQueryHandler(IApiKeyStore store)
{
    /// <summary>Returns the session's key status and mask.</summary>
    public Task<ApiKeyStatusResponse> Handle(GetApiKeyStatusQuery query, CancellationToken cancellationToken)
    {
        string? apiKey = store.Get();

        ApiKeyStatusResponse response = apiKey is null
            ? ApiKeyStatusResponse.None()
            : ApiKeyStatusResponse.Active(ApiKeyMask.Mask(apiKey));

        return Task.FromResult(response);
    }
}

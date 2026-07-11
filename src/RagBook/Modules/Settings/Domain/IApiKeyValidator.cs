namespace RagBook.Modules.Settings.Domain;

/// <summary>
/// Narrow seam over the external provider used to prove a candidate key is live and authorized
/// (constitution §V). The implementation performs a non-generative, auth-checked call and is wrapped
/// in resilience; tests swap an in-memory fake so no test hits the real provider.
/// </summary>
public interface IApiKeyValidator
{
    /// <summary>Validates <paramref name="apiKey"/> upstream and classifies the outcome.</summary>
    Task<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken cancellationToken);
}

using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Settings.Features.SetApiKey;

/// <summary>
/// Handles <see cref="SetApiKeyCommand"/>. Order is deliberate: (1) throttle the session so a blocked
/// attempt never reaches the provider (FR-004b); (2) reject an empty/malformed key locally — same
/// <c>settings.invalid_api_key</c> code as a provider rejection, so the frontend maps one message
/// (FR-003/FR-004; the local check lives here, not in FluentValidation, to keep the stable code); (3)
/// validate upstream and act on the three-way outcome. The key is stored only on <see cref="ApiKeyValidationResult.Valid"/>.
/// </summary>
public sealed class SetApiKeyCommandHandler(
    IApiKeyThrottle throttle,
    IApiKeyValidator validator,
    IApiKeyStore store)
{
    private const string RequiredPrefix = "sk-ant-";

    /// <summary>Stores the validated key and returns the active status + mask, or a domain error.</summary>
    public async Task<Result<ApiKeyStatusResponse>> Handle(SetApiKeyCommand command, CancellationToken cancellationToken)
    {
        if (!throttle.TryRegisterAttempt())
        {
            return Result.Failure<ApiKeyStatusResponse>(SettingsErrors.TooManyAttempts);
        }

        string apiKey = command.ApiKey?.Trim() ?? string.Empty;
        if (!IsWellFormed(apiKey))
        {
            return Result.Failure<ApiKeyStatusResponse>(SettingsErrors.InvalidApiKey);
        }

        ApiKeyValidationResult validation = await validator.ValidateAsync(apiKey, cancellationToken);

        return validation switch
        {
            ApiKeyValidationResult.Valid => Store(apiKey),
            ApiKeyValidationResult.Rejected => Result.Failure<ApiKeyStatusResponse>(SettingsErrors.InvalidApiKey),
            _ => Result.Failure<ApiKeyStatusResponse>(SettingsErrors.ValidationUnavailable),
        };
    }

    private static bool IsWellFormed(string apiKey)
    {
        return apiKey.Length >= 8 && apiKey.StartsWith(RequiredPrefix, StringComparison.Ordinal);
    }

    private Result<ApiKeyStatusResponse> Store(string apiKey)
    {
        store.Set(apiKey);

        return ApiKeyStatusResponse.Active(ApiKeyMask.Mask(apiKey));
    }
}

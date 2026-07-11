using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Messaging;

namespace RagBook.Modules.Settings.Features.SetApiKey;

/// <summary>Validates and stores the user's BYOK generation key for the current session (US-02 AC-1).</summary>
/// <param name="ApiKey">The full provider key; carried in the POST body only, never logged.</param>
public sealed record SetApiKeyCommand(string ApiKey) : ICommand<ApiKeyStatusResponse>;

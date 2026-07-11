using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Messaging;

namespace RagBook.Modules.Settings.Features.GetApiKeyStatus;

/// <summary>Reports whether the current session has an active key, and its mask if so (US-02 AC-2, FR-007).</summary>
public sealed record GetApiKeyStatusQuery : IQuery<ApiKeyStatusResponse>;

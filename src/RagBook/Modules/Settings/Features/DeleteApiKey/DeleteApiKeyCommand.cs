using RagBook.Shared.Messaging;

namespace RagBook.Modules.Settings.Features.DeleteApiKey;

/// <summary>Removes the current session's stored key; idempotent when none is present (US-02 AC-4).</summary>
public sealed record DeleteApiKeyCommand : ICommand;

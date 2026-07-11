using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Results;

namespace RagBook.Modules.Settings.Features.DeleteApiKey;

/// <summary>
/// Handles <see cref="DeleteApiKeyCommand"/>. Removing is idempotent — deleting when no key is stored
/// still succeeds (US-02 AC-4), so double-clicks and repeat calls are safe.
/// </summary>
public sealed class DeleteApiKeyCommandHandler(IApiKeyStore store)
{
    /// <summary>Removes the session's key and returns success unconditionally.</summary>
    public Task<Result> Handle(DeleteApiKeyCommand command, CancellationToken cancellationToken)
    {
        store.Remove();

        return Task.FromResult(Result.Success());
    }
}

using RagBook.Shared.Results;

namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The scoped retrieval engine (US-13): given a <see cref="ChatScope"/> and a question, it returns the
/// most relevant passages within that scope. It validates the scope against the session, short-circuits an
/// empty scope before embedding, embeds the question through the centralised provider (US-06), and runs a
/// pgvector similarity search pre-filtered by session + ready-status + scope. Consumed by US-14 (chat).
/// </summary>
public interface IScopedRetriever
{
    /// <summary>
    /// Retrieves passages for <paramref name="scope"/> and <paramref name="question"/>. Returns
    /// <see cref="Errors.ChatErrors.ScopeNotFound"/> when a folder/document target is not visible to the
    /// session; a <see cref="ScopedRetrievalResult.Empty"/> success when the scope has no ready content;
    /// otherwise the ranked matches.
    /// </summary>
    Task<Result<ScopedRetrievalResult>> RetrieveAsync(ChatScope scope, string question, CancellationToken cancellationToken);
}

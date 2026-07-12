namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The outcome of a scoped retrieval (US-13). A valid scope with no ready-indexed content yields
/// <see cref="IsEmptyScope"/> (the caller shows "no documents in the selected scope" without generating);
/// otherwise <see cref="Matches"/> holds the passages, ordered closest-first and capped at the configured
/// limit. A not-visible scope target is not represented here — that is a <c>chat.scope_not_found</c> failure.
/// </summary>
public sealed record ScopedRetrievalResult
{
    private ScopedRetrievalResult(bool isEmptyScope, IReadOnlyList<RetrievedChunk> matches)
    {
        IsEmptyScope = isEmptyScope;
        Matches = matches;
    }

    /// <summary>True when the scope has no ready-indexed content — decided before embedding or search.</summary>
    public bool IsEmptyScope { get; }

    /// <summary>The retrieved passages, ordered by ascending distance; empty when <see cref="IsEmptyScope"/>.</summary>
    public IReadOnlyList<RetrievedChunk> Matches { get; }

    /// <summary>The result for a scope with no ready-indexed content.</summary>
    public static ScopedRetrievalResult Empty { get; } = new(isEmptyScope: true, []);

    /// <summary>The result carrying the retrieved <paramref name="matches"/>.</summary>
    public static ScopedRetrievalResult From(IReadOnlyList<RetrievedChunk> matches)
    {
        return new ScopedRetrievalResult(isEmptyScope: false, matches);
    }
}

namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// Streams a grounded answer from the generation provider (US-14). Yields answer text **deltas** as the
/// provider produces them; throws <see cref="AnswerGenerationException"/> with a distinct
/// <see cref="AnswerGenerationFailure"/> on failure. The concrete client is reached only through this seam
/// (constitution §V) so tests swap a deterministic streaming fake — no test hits the real provider.
/// </summary>
public interface IAnswerGenerator
{
    /// <summary>Streams the answer for <paramref name="context"/> as incremental text deltas.</summary>
    IAsyncEnumerable<string> GenerateAsync(GroundedContext context, CancellationToken cancellationToken);
}

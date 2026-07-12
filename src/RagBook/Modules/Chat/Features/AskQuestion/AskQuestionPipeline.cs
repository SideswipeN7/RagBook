using Microsoft.Extensions.Options;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Errors;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Results;

namespace RagBook.Modules.Chat.Features.AskQuestion;

/// <summary>
/// The pre-generation ask pipeline (US-14): validate → retrieve (US-13) → similarity cutoff → build the
/// grounding context. Returns <see cref="AskOutcome.Answerable"/> when grounds survive the threshold,
/// <see cref="AskOutcome.InsufficientGrounding"/> for an empty scope or all-below-threshold (the model is
/// NOT called), or a failure for an invalid question / not-visible scope. The question is validated here
/// (handler-owned) so the stable <c>chat.invalid_question</c> code is emitted, not a generic validation error.
/// </summary>
public sealed class AskQuestionPipeline(
    IScopedRetriever retriever,
    IPromptBuilder promptBuilder,
    IOptions<RagOptions> options)
    : IAskQuestionPipeline
{
    /// <inheritdoc />
    public async Task<Result<AskOutcome>> PrepareAsync(string question, ChatScope scope, CancellationToken cancellationToken)
    {
        RagOptions rag = options.Value;

        string trimmed = question?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > rag.MaxQuestionChars)
        {
            return Result.Failure<AskOutcome>(ChatErrors.InvalidQuestion);
        }

        Result<ScopedRetrievalResult> retrieval = await retriever.RetrieveAsync(scope, trimmed, cancellationToken);
        if (retrieval.IsFailure)
        {
            return Result.Failure<AskOutcome>(retrieval.Error);
        }

        ScopedRetrievalResult result = retrieval.Value;
        if (result.IsEmptyScope)
        {
            return AskOutcome.InsufficientGrounding;
        }

        // Keep only passages relevant enough: cosine similarity >= threshold  <=>  distance <= 1 - threshold.
        double maxDistance = 1.0 - rag.SimilarityThreshold;
        List<RetrievedChunk> grounded = result.Matches.Where(match => match.Distance <= maxDistance).ToList();
        if (grounded.Count == 0)
        {
            return AskOutcome.InsufficientGrounding;
        }

        GroundedContext context = promptBuilder.Build(trimmed, grounded);

        // Defensive: if the context budget trimmed away every passage, there is nothing to ground on.
        if (context.Sources.Count == 0)
        {
            return AskOutcome.InsufficientGrounding;
        }

        return AskOutcome.Answerable(context);
    }
}

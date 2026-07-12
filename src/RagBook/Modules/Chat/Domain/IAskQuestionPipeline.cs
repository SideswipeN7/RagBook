using RagBook.Shared.Results;

namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The pre-generation ask pipeline (US-14): validate the question, retrieve within the scope (US-13), apply
/// the similarity cutoff, and build the grounding context — returning either an <see cref="AskOutcome"/>
/// (answerable / insufficient) or a failure (<c>chat.invalid_question</c>, <c>chat.scope_not_found</c>). The
/// key guard and the token streaming are driven separately by the endpoint.
/// </summary>
public interface IAskQuestionPipeline
{
    /// <summary>Validates + retrieves + thresholds + builds; returns the outcome or a domain failure.</summary>
    Task<Result<AskOutcome>> PrepareAsync(string question, ChatScope scope, CancellationToken cancellationToken);
}

using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Outcome of an all-or-nothing bulk operation (US-12). The single-<see cref="Error"/> <see cref="Result"/>
/// cannot carry a per-id list, so a bulk handler returns one of three shapes and the endpoint maps each to a
/// single code-based wire outcome (constitution §II):
/// <list type="bullet">
/// <item><see cref="Success"/> → <c>204 No Content</c>.</item>
/// <item><see cref="BadRequest"/>(<see cref="Error"/>) → a plain <c>400</c> ProblemDetails (empty / over-cap list).</item>
/// <item><see cref="ValidationFailed"/>(failures) → a <c>422</c> ProblemDetails with a <c>failures[]</c> extension.</item>
/// </list>
/// </summary>
public sealed class BulkResult
{
    private BulkResult(BulkOutcome outcome, Error? error, IReadOnlyList<BulkFailure> failures)
    {
        Outcome = outcome;
        Error = error;
        Failures = failures;
    }

    /// <summary>Which of the three wire outcomes this result represents.</summary>
    public BulkOutcome Outcome { get; }

    /// <summary>The single validation error for a <see cref="BulkOutcome.BadRequest"/> (empty / over-cap); otherwise <c>null</c>.</summary>
    public Error? Error { get; }

    /// <summary>The per-id failures for a <see cref="BulkOutcome.ValidationFailed"/>; empty otherwise.</summary>
    public IReadOnlyList<BulkFailure> Failures { get; }

    /// <summary>All items validated and the operation was applied (→ 204).</summary>
    public static BulkResult Success() =>
        new(BulkOutcome.Success, error: null, failures: []);

    /// <summary>The request itself is malformed — an empty or over-cap id list (→ 400).</summary>
    public static BulkResult BadRequest(Error error) =>
        new(BulkOutcome.BadRequest, error, failures: []);

    /// <summary>One or more items failed validation; nothing was changed (→ 422 + <c>failures[]</c>).</summary>
    public static BulkResult ValidationFailed(IReadOnlyList<BulkFailure> failures) =>
        new(BulkOutcome.ValidationFailed, error: null, failures);
}

/// <summary>The three mutually exclusive outcomes of a <see cref="BulkResult"/>.</summary>
public enum BulkOutcome
{
    /// <summary>Applied to every item (→ 204).</summary>
    Success,

    /// <summary>Malformed request — empty / over-cap list (→ 400).</summary>
    BadRequest,

    /// <summary>Per-id validation failure, nothing changed (→ 422 + failures).</summary>
    ValidationFailed,
}

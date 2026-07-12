namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The outcome of preparing an ask (US-14), before streaming. Either the question is
/// <see cref="Answerable"/> (grounds found — stream the answer) or there is
/// <see cref="InsufficientGrounding"/> (empty scope or all matches below threshold — do NOT call the
/// model). A not-visible scope target and an invalid question are failures, not outcomes.
/// </summary>
public sealed record AskOutcome
{
    private AskOutcome(bool isAnswerable, GroundedContext? context)
    {
        IsAnswerable = isAnswerable;
        Context = context;
    }

    /// <summary>True when grounds were found and the answer should be generated.</summary>
    public bool IsAnswerable { get; }

    /// <summary>The grounding context when <see cref="IsAnswerable"/>; otherwise <c>null</c>.</summary>
    public GroundedContext? Context { get; }

    /// <summary>Grounds found — stream the answer from <paramref name="context"/>.</summary>
    public static AskOutcome Answerable(GroundedContext context)
    {
        return new AskOutcome(isAnswerable: true, context);
    }

    /// <summary>No sufficient grounds — return the "no documents/basis" signal without calling the model.</summary>
    public static AskOutcome InsufficientGrounding { get; } = new(isAnswerable: false, context: null);
}

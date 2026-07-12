namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// The assembled grounding for an answerable question (US-14): the numbered source passages, plus the
/// system instructions and the user message the generator sends. The user message is trimmed to the
/// configured context budget (weakest passages dropped first); the numbering matches the <c>sources</c>
/// event so <c>[n]</c> references resolve (US-16).
/// </summary>
/// <param name="Sources">The numbered passages, most-relevant first.</param>
/// <param name="SystemPrompt">The grounding instructions (answer only from passages, cite [n], refuse-if-unsupported, question's language).</param>
/// <param name="UserPrompt">The numbered passages followed by the question.</param>
public sealed record GroundedContext(
    IReadOnlyList<GroundingPassage> Sources,
    string SystemPrompt,
    string UserPrompt);

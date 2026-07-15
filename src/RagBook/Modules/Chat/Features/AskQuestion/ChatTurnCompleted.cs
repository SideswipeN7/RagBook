using RagBook.Shared.Messaging;

namespace RagBook.Modules.Chat.Features.AskQuestion;

/// <summary>
/// Published after an ask's answer stream ends (US-18) — including a client disconnect (interrupted). Routed to
/// the durable outbox so the assistant message is persisted off the stream's hot path and survives a crash.
/// </summary>
/// <param name="ConversationId">The conversation the turn belongs to.</param>
/// <param name="UserSessionId">The owning session — the handler runs outside the request, so it initializes this.</param>
/// <param name="Answer">The accumulated answer text (partial when interrupted; empty for a deterministic no-answer).</param>
/// <param name="State">The final state — <c>answered</c> | <c>no_answer</c> | <c>interrupted</c>.</param>
/// <param name="SourcesJson">The assistant citations as a <c>SourceDto[]</c> JSON document, or <c>null</c>.</param>
public sealed record ChatTurnCompleted(
    Guid ConversationId,
    Guid UserSessionId,
    string Answer,
    string State,
    string? SourcesJson) : IExternalEvent;

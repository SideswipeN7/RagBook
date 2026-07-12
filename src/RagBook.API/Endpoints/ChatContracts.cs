namespace RagBook.API.Endpoints;

/// <summary>Request body for <c>POST /api/chat/ask</c> (US-14). The question is in the body, never the URL.</summary>
/// <param name="Question">The natural-language question.</param>
/// <param name="Scope">The search scope for retrieval.</param>
public sealed record AskQuestionRequest(string Question, ScopeDto Scope);

/// <summary>The scope selector for an ask.</summary>
/// <param name="Type">One of <c>all</c> | <c>folder</c> | <c>document</c>.</param>
/// <param name="TargetId">The folder/document id (required for <c>folder</c>/<c>document</c>; absent for <c>all</c>).</param>
public sealed record ScopeDto(string Type, Guid? TargetId);

/// <summary>Payload of a `sources` SSE event — one numbered grounding passage's provenance (US-16 resolves <c>[n]</c>).</summary>
/// <param name="Number">The <c>[n]</c> number.</param>
/// <param name="DocumentId">The source document.</param>
/// <param name="FileName">The source document's file name.</param>
/// <param name="PageNumber">Source page (null for TXT/MD).</param>
public sealed record SourceDto(int Number, Guid DocumentId, string FileName, int? PageNumber);

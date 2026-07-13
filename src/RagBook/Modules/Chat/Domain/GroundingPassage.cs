namespace RagBook.Modules.Chat.Domain;

/// <summary>
/// A retrieved passage that survived the similarity cutoff and grounds the answer (US-14). Numbered
/// <c>[n]</c> for the prompt and for citations (US-16); carries its source document + page.
/// </summary>
/// <param name="Number">1-based source number, most-relevant first.</param>
/// <param name="DocumentId">The source document id.</param>
/// <param name="FileName">The source document's file name.</param>
/// <param name="PageNumber">Source page for PDFs; <c>null</c> for TXT/MD.</param>
/// <param name="Text">The passage text.</param>
/// <param name="ChunkId">The source chunk's id — the deterministic <c>[n]</c>→chunk mapping key (US-16).</param>
public sealed record GroundingPassage(int Number, Guid DocumentId, string FileName, int? PageNumber, string Text, Guid ChunkId);

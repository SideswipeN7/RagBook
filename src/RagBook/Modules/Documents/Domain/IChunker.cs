namespace RagBook.Modules.Documents.Domain;

/// <summary>A produced chunk: its position, text, and source page (for citations).</summary>
/// <param name="Index">0-based position in the document.</param>
/// <param name="Text">The chunk text.</param>
/// <param name="PageNumber">Source page (PDF), or <c>null</c>.</param>
public sealed record TextChunk(int Index, string Text, int? PageNumber);

/// <summary>
/// Splits extracted text into overlapping structural chunks (US-06). Pure and deterministic: it
/// normalizes, packs segments to a configured target size with a configured overlap, keeps each chunk's
/// source page number, and yields at least one chunk (no overlap) for very short text.
/// </summary>
public interface IChunker
{
    /// <summary>Chunks the ordered <paramref name="segments"/> into positioned <see cref="TextChunk"/>s.</summary>
    IReadOnlyList<TextChunk> Chunk(IReadOnlyList<ExtractedSegment> segments);
}

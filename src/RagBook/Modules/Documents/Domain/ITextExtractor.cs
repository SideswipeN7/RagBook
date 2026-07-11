namespace RagBook.Modules.Documents.Domain;

/// <summary>A contiguous piece of extracted text with its source page (PDF) when known.</summary>
/// <param name="PageNumber">1-based source page, or <c>null</c> (TXT/MD).</param>
/// <param name="Text">The raw extracted text of the segment.</param>
public sealed record ExtractedSegment(int? PageNumber, string Text);

/// <summary>The full extracted text of a document as ordered segments.</summary>
/// <param name="Segments">Segments in document order (pages for PDF; the whole file for TXT/MD).</param>
public sealed record ExtractedText(IReadOnlyList<ExtractedSegment> Segments);

/// <summary>
/// Extracts text from a document's binary (US-06). One implementation per content type, dispatched by a
/// resolver. An implementation returns the extracted segments, or throws / returns empty when the file
/// has no extractable text (encrypted/scan/corrupt) — the handler treats that as unreadable (AC-2).
/// </summary>
public interface ITextExtractor
{
    /// <summary>True when this extractor handles <paramref name="contentType"/>.</summary>
    bool CanExtract(string contentType);

    /// <summary>Extracts the document's text from <paramref name="content"/>.</summary>
    Task<ExtractedText> ExtractAsync(Stream content, CancellationToken cancellationToken);
}

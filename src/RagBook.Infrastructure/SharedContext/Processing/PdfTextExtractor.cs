using RagBook.Modules.Documents.Domain;
using UglyToad.PdfPig;

namespace RagBook.Infrastructure.SharedContext.Processing;

/// <summary>
/// <see cref="ITextExtractor"/> for PDF (US-06) using UglyToad.PdfPig: one segment per page (keeping the
/// 1-based page number for citations). An encrypted/corrupt PDF makes <c>PdfDocument.Open</c> throw and a
/// scan-only PDF yields empty page text — both surface as unreadable (the handler marks the document
/// failed). No OCR (out of scope).
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    /// <inheritdoc />
    public bool CanExtract(string contentType)
    {
        return contentType == "application/pdf";
    }

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        using PdfDocument document = PdfDocument.Open(buffer.ToArray());

        var segments = new List<ExtractedSegment>();
        foreach (var page in document.GetPages())
        {
            segments.Add(new ExtractedSegment(page.Number, page.Text));
        }

        return new ExtractedText(segments);
    }
}

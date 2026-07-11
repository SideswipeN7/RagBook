using System.Text;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Infrastructure.SharedContext.Processing;

/// <summary>
/// <see cref="ITextExtractor"/> for TXT/Markdown (US-06): reads the content as UTF-8 into a single
/// segment (no page numbers). Empty content yields an empty segment, which the handler treats as
/// unreadable.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    /// <inheritdoc />
    public bool CanExtract(string contentType)
    {
        return contentType is "text/plain" or "text/markdown";
    }

    /// <inheritdoc />
    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Encoding.UTF8);
        string text = await reader.ReadToEndAsync(cancellationToken);

        return new ExtractedText([new ExtractedSegment(PageNumber: null, text)]);
    }
}

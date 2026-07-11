using System.Text;
using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;

namespace RagBook.Modules.Documents.Processing;

/// <summary>
/// Default <see cref="IChunker"/> (US-06). Pure and deterministic: it normalizes each segment (strips
/// control characters, collapses whitespace), then produces overlapping windows of
/// <see cref="ChunkingOptions.TargetChars"/> stepping by <c>Target − Overlap</c>. A segment at or below
/// the target yields a single chunk with **no** overlap; each chunk keeps its segment's page number for
/// citations. Empty/blank text yields no chunks (the handler then treats the document as unreadable).
/// </summary>
public sealed class StructuralChunker(IOptions<ChunkingOptions> options) : IChunker
{
    private readonly ChunkingOptions _options = options.Value;

    /// <inheritdoc />
    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<ExtractedSegment> segments)
    {
        var chunks = new List<TextChunk>();
        int index = 0;

        foreach (ExtractedSegment segment in segments)
        {
            string normalized = Normalize(segment.Text);
            if (normalized.Length == 0)
            {
                continue;
            }

            foreach (string piece in Window(normalized))
            {
                chunks.Add(new TextChunk(index++, piece, segment.PageNumber));
            }
        }

        return chunks;
    }

    private IEnumerable<string> Window(string text)
    {
        int target = Math.Max(1, _options.TargetChars);
        if (text.Length <= target)
        {
            yield return text;
            yield break;
        }

        int step = Math.Max(1, target - Math.Max(0, _options.OverlapChars));
        for (int start = 0; start < text.Length; start += step)
        {
            int length = Math.Min(target, text.Length - start);
            yield return text.Substring(start, length);

            if (start + length >= text.Length)
            {
                yield break;
            }
        }
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool lastWasSpace = false;

        foreach (char character in text)
        {
            if (char.IsControl(character) && character is not '\t' and not '\n' and not '\r')
            {
                continue; // strip binary/control noise
            }

            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }
}

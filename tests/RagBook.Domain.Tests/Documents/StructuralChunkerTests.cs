using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Processing;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

public sealed class StructuralChunkerTests
{
    private static StructuralChunker CreateSut(int target = 100, int overlap = 20)
    {
        return new StructuralChunker(Options.Create(new ChunkingOptions { TargetChars = target, OverlapChars = overlap }));
    }

    [Fact]
    public void Should_ProduceSingleChunkNoOverlap_When_ShortText()
    {
        // Arrange (FR-005 edge)
        var sut = CreateSut(target: 100, overlap: 20);
        var segments = new List<ExtractedSegment> { new(PageNumber: null, "Krotki akapit.") };

        // Act
        IReadOnlyList<TextChunk> chunks = sut.Chunk(segments);

        // Assert
        chunks.Should().ContainSingle();
        chunks[0].Text.Should().Be("Krotki akapit.");
    }

    [Fact]
    public void Should_SplitBySizeWithOverlap_When_LongText()
    {
        // Arrange — 250 chars, target 100, overlap 20 → step 80 → starts 0,80,160,240.
        var sut = CreateSut(target: 100, overlap: 20);
        string text = new('a', 250);
        var segments = new List<ExtractedSegment> { new(PageNumber: null, text) };

        // Act
        IReadOnlyList<TextChunk> chunks = sut.Chunk(segments);

        // Assert — multiple chunks; consecutive chunks overlap by 20 (chunk0 ends at 100, chunk1 starts at 80).
        chunks.Count.Should().BeGreaterThan(1);
        chunks[0].Text.Length.Should().Be(100);
        chunks.Select(chunk => chunk.Index).Should().Equal(Enumerable.Range(0, chunks.Count));
        chunks[0].Text[^20..].Should().Be(chunks[1].Text[..20]);
    }

    [Fact]
    public void Should_KeepPageNumbers_When_PdfSegments()
    {
        // Arrange
        var sut = CreateSut(target: 1000, overlap: 150);
        var segments = new List<ExtractedSegment>
        {
            new(PageNumber: 1, "Tresc strony pierwszej."),
            new(PageNumber: 2, "Tresc strony drugiej."),
        };

        // Act
        IReadOnlyList<TextChunk> chunks = sut.Chunk(segments);

        // Assert
        chunks.Should().HaveCount(2);
        chunks[0].PageNumber.Should().Be(1);
        chunks[1].PageNumber.Should().Be(2);
    }

    [Fact]
    public void Should_CollapseWhitespace_When_Normalizing()
    {
        // Arrange — runs of spaces and newlines.
        var sut = CreateSut(target: 1000, overlap: 150);
        string text = "a b" + new string(' ', 3) + "c" + new string('\n', 3) + "d";
        var segments = new List<ExtractedSegment> { new(PageNumber: null, text) };

        // Act
        IReadOnlyList<TextChunk> chunks = sut.Chunk(segments);

        // Assert
        chunks.Should().ContainSingle();
        chunks[0].Text.Should().Be("a b c d");
    }

    [Fact]
    public void Should_StripControlChars_When_Normalizing()
    {
        // Arrange — a NUL control character between "a" and "b".
        var sut = CreateSut(target: 1000, overlap: 150);
        string withNul = "a" + (char)0 + "b";
        var segments = new List<ExtractedSegment> { new(PageNumber: null, withNul) };

        // Act
        IReadOnlyList<TextChunk> chunks = sut.Chunk(segments);

        // Assert — the control char is removed.
        chunks.Should().ContainSingle();
        chunks[0].Text.Should().Be("ab");
    }

    [Fact]
    public void Should_ProduceNoChunks_When_TextIsBlank()
    {
        // Arrange
        var sut = CreateSut();
        string blank = new string(' ', 3) + "\n\t";
        var segments = new List<ExtractedSegment> { new(PageNumber: null, blank) };

        // Act
        IReadOnlyList<TextChunk> chunks = sut.Chunk(segments);

        // Assert — the handler treats zero chunks as unreadable.
        chunks.Should().BeEmpty();
    }
}

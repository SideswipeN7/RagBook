using FluentAssertions;
using Microsoft.Extensions.Options;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Documents.Domain;
using Xunit;

namespace RagBook.Application.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="PromptBuilder"/> (US-14). Passages are numbered <c>[1..K]</c> with file+page,
/// the system prompt carries the grounding instructions (only-from-passages, cite <c>[n]</c>, refusal phrase,
/// question's language), and the context is trimmed to <see cref="RagOptions.MaxContextChars"/> weakest-first.
/// </summary>
public sealed class PromptBuilderTests
{
    private static RetrievedChunk Chunk(string fileName, int? page, string text, double distance)
    {
        return new RetrievedChunk(Guid.NewGuid(), Guid.NewGuid(), fileName, text, page, distance);
    }

    private static PromptBuilder CreateSut(int maxContextChars = 8000)
    {
        return new PromptBuilder(Options.Create(new RagOptions { MaxContextChars = maxContextChars }));
    }

    [Fact]
    public void Should_CarryChunkId_And_Text_OnEachSource()
    {
        // Arrange (US-16) — the citation mapping key must be the chunk's id, from the prompt data.
        var chunkId = Guid.NewGuid();
        var passage = new RetrievedChunk(chunkId, Guid.NewGuid(), "a.pdf", "pełny tekst chunka", 1, 0.1);

        // Act
        GroundedContext context = CreateSut().Build("q", [passage]);

        // Assert
        context.Sources[0].ChunkId.Should().Be(chunkId);
        context.Sources[0].Text.Should().Be("pełny tekst chunka");
    }

    [Fact]
    public void Should_NumberPassages_MostRelevantFirst_WithFileAndPage()
    {
        // Arrange
        var passages = new[] { Chunk("umowa.pdf", 3, "alpha", 0.1), Chunk("aneks.md", null, "beta", 0.2) };
        PromptBuilder sut = CreateSut();

        // Act
        GroundedContext context = sut.Build("Jaki jest okres?", passages);

        // Assert
        context.Sources.Select(source => source.Number).Should().Equal(1, 2);
        context.Sources[0].FileName.Should().Be("umowa.pdf");
        context.Sources[0].PageNumber.Should().Be(3);
        context.UserPrompt.Should().Contain("[1] (umowa.pdf, s. 3): alpha");
        context.UserPrompt.Should().Contain("[2] (aneks.md): beta");
        context.UserPrompt.Should().Contain("Jaki jest okres?");
    }

    [Fact]
    public void Should_CarryGroundingInstructions_Refusal_And_LanguageRule()
    {
        // Arrange
        PromptBuilder sut = CreateSut();

        // Act
        GroundedContext context = sut.Build("q", new[] { Chunk("a.pdf", null, "text", 0.1) });

        // Assert
        context.SystemPrompt.Should().Contain("ONLY");
        context.SystemPrompt.Should().Contain(GroundingPrompt.RefusalPhrase);
        context.SystemPrompt.Should().Contain("same language");
        context.SystemPrompt.Should().Contain("[");
    }

    [Fact]
    public void Should_DropWeakestPassages_When_ExceedingMaxContextChars()
    {
        // Arrange — three passages; a tiny budget keeps only the strongest (first).
        var passages = new[]
        {
            Chunk("a.pdf", null, new string('A', 40), 0.1),
            Chunk("b.pdf", null, new string('B', 40), 0.2),
            Chunk("c.pdf", null, new string('C', 40), 0.3),
        };
        PromptBuilder sut = CreateSut(maxContextChars: 90);

        // Act
        GroundedContext context = sut.Build("q", passages);

        // Assert — weakest dropped first; the strongest survives, and the budget holds.
        context.Sources.Should().HaveCountLessThan(3);
        context.Sources.Should().Contain(source => source.FileName == "a.pdf");
        context.Sources.Should().NotContain(source => source.FileName == "c.pdf");
        context.UserPrompt.Length.Should().BeLessThanOrEqualTo(90);
    }
}

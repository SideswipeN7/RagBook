using FluentAssertions;
using RagBook.Modules.Chat.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="GroundingPrompt.IsRefusal"/> (US-17): the refusal sentinel is matched as the
/// whole, trimmed answer (equality) — not a substring or prefix — so partial/embedded cases stay normal.
/// </summary>
public sealed class GroundingPromptTests
{
    [Fact]
    public void Should_BeRefusal_When_AnswerIsExactSentinel()
    {
        // Act
        bool result = GroundingPrompt.IsRefusal(GroundingPrompt.RefusalPhrase);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Should_BeRefusal_When_SentinelHasSurroundingWhitespace()
    {
        // Arrange
        string answer = $"  \n{GroundingPrompt.RefusalPhrase}\n  ";

        // Act
        bool result = GroundingPrompt.IsRefusal(answer);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Should_NotBeRefusal_When_SentinelAppearsMidText()
    {
        // Arrange — the phrase is embedded in a longer, otherwise-substantive answer.
        string answer = $"Umowa nie zawiera kary umownej [1]. {GroundingPrompt.RefusalPhrase}";

        // Act
        bool result = GroundingPrompt.IsRefusal(answer);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Should_NotBeRefusal_When_AnswerOpensWithSentinelThenContinues()
    {
        // Arrange
        string answer = $"{GroundingPrompt.RefusalPhrase} Ale znalazłem coś innego [1].";

        // Act
        bool result = GroundingPrompt.IsRefusal(answer);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Should_NotBeRefusal_When_PartialAnswerWithCitation()
    {
        // Act
        bool result = GroundingPrompt.IsRefusal("Zawiera A [1]; brak informacji o B.");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Should_NotBeRefusal_When_NormalAnswer()
    {
        // Act
        bool result = GroundingPrompt.IsRefusal("Okres wypowiedzenia wynosi 3 miesiące [1].");

        // Assert
        result.Should().BeFalse();
    }
}

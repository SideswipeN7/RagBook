using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RagBook.Modules.Chat;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Chat.Errors;
using RagBook.Modules.Chat.Features.AskQuestion;
using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Chat;

/// <summary>
/// Unit tests for <see cref="AskQuestionPipeline"/> (US-14). Validates the question, thresholds the retrieved
/// matches (cosine similarity ≥ <see cref="RagOptions.SimilarityThreshold"/>), and returns Answerable /
/// InsufficientGrounding / a failure — with a mocked retriever + a real <see cref="PromptBuilder"/>.
/// </summary>
public sealed class AskQuestionPipelineTests
{
    private readonly IScopedRetriever _retriever = Substitute.For<IScopedRetriever>();

    private static RetrievedChunk Chunk(double distance)
    {
        return new RetrievedChunk(Guid.NewGuid(), Guid.NewGuid(), "a.pdf", "text", null, distance);
    }

    private AskQuestionPipeline CreateSut(double threshold = 0.75)
    {
        var options = Options.Create(new RagOptions { SimilarityThreshold = threshold });

        return new AskQuestionPipeline(_retriever, new PromptBuilder(options), options);
    }

    private void RetrieverReturns(ScopedRetrievalResult result)
    {
        _retriever.RetrieveAsync(Arg.Any<ChatScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(result));
    }

    [Fact]
    public async Task Should_ReturnAnswerable_When_MatchesAboveThreshold()
    {
        // Arrange — distance 0.1 ⇒ similarity 0.9 ≥ 0.75.
        RetrieverReturns(ScopedRetrievalResult.From([Chunk(0.1)]));
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync("What is the term?", ChatScope.All(), [], CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAnswerable.Should().BeTrue();
        result.Value.Context!.Sources.Should().HaveCount(1);
    }

    [Fact]
    public async Task Should_MarkContextIsDemo_When_ScopeIsDemo()
    {
        // Arrange — an answerable demo-scoped question (US-03): the grounded context must carry IsDemo so the
        // generator uses the application key, not the session's BYOK key.
        RetrieverReturns(ScopedRetrievalResult.From([Chunk(0.1)]));
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync("What is in the demo?", ChatScope.Demo(), [], CancellationToken.None);

        // Assert
        result.Value.IsAnswerable.Should().BeTrue();
        result.Value.Context!.IsDemo.Should().BeTrue();
    }

    [Fact]
    public async Task Should_NotMarkContextIsDemo_When_ScopeIsNotDemo()
    {
        // Arrange
        RetrieverReturns(ScopedRetrievalResult.From([Chunk(0.1)]));
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync("What is the term?", ChatScope.All(), [], CancellationToken.None);

        // Assert
        result.Value.Context!.IsDemo.Should().BeFalse();
    }

    [Fact]
    public async Task Should_ReturnInsufficient_When_AllBelowThreshold()
    {
        // Arrange — distance 0.5 ⇒ similarity 0.5 < 0.75.
        RetrieverReturns(ScopedRetrievalResult.From([Chunk(0.5)]));
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync("unrelated", ChatScope.All(), [], CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsAnswerable.Should().BeFalse();
    }

    [Fact]
    public async Task Should_ReturnInsufficient_When_EmptyScope()
    {
        // Arrange
        RetrieverReturns(ScopedRetrievalResult.Empty);
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync("q", ChatScope.All(), [], CancellationToken.None);

        // Assert
        result.Value.IsAnswerable.Should().BeFalse();
    }

    [Fact]
    public async Task Should_PropagateScopeNotFound()
    {
        // Arrange
        _retriever.RetrieveAsync(Arg.Any<ChatScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ScopedRetrievalResult>(ChatErrors.ScopeNotFound));
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync("q", ChatScope.Folder(Guid.NewGuid()), [], CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatErrors.ScopeNotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_ReturnInvalidQuestion_When_EmptyOrWhitespace(string question)
    {
        // Arrange
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync(question, ChatScope.All(), [], CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatErrors.InvalidQuestion);
        await _retriever.DidNotReceive().RetrieveAsync(Arg.Any<ChatScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnInvalidQuestion_When_TooLong()
    {
        // Arrange — over the default 2000-char maximum.
        AskQuestionPipeline sut = CreateSut();

        // Act
        Result<AskOutcome> result = await sut.PrepareAsync(new string('x', 2001), ChatScope.All(), [], CancellationToken.None);

        // Assert
        result.Error.Should().Be(ChatErrors.InvalidQuestion);
    }

    [Fact]
    public async Task Should_FlipAnswerableVsInsufficient_When_ThresholdChanged()
    {
        // Arrange — a match at similarity 0.7 (distance 0.3): answerable at 0.6, insufficient at 0.8 (A1/AC-4).
        RetrieverReturns(ScopedRetrievalResult.From([Chunk(0.3)]));

        // Act
        Result<AskOutcome> loose = await CreateSut(threshold: 0.6).PrepareAsync("q", ChatScope.All(), [], CancellationToken.None);
        RetrieverReturns(ScopedRetrievalResult.From([Chunk(0.3)]));
        Result<AskOutcome> strict = await CreateSut(threshold: 0.8).PrepareAsync("q", ChatScope.All(), [], CancellationToken.None);

        // Assert
        loose.Value.IsAnswerable.Should().BeTrue();
        strict.Value.IsAnswerable.Should().BeFalse();
    }
}

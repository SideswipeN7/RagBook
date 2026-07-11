using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Features.GetApiKeyStatus;
using Xunit;

namespace RagBook.Application.Tests.Settings;

/// <summary>
/// Unit tests for <see cref="GetApiKeyStatusQueryHandler"/> (US-02 AC-2, FR-007). The projection is
/// <c>none</c> or <c>active</c> + mask, and never carries the full key.
/// </summary>
public sealed class GetApiKeyStatusQueryHandlerTests
{
    private readonly IApiKeyStore _store = Substitute.For<IApiKeyStore>();

    private GetApiKeyStatusQueryHandler CreateSut()
    {
        return new GetApiKeyStatusQueryHandler(_store);
    }

    [Fact]
    public async Task Should_ReturnNone_When_NoKey()
    {
        // Arrange
        _store.Get().Returns((string?)null);
        var sut = CreateSut();

        // Act
        ApiKeyStatusResponse response = await sut.Handle(new GetApiKeyStatusQuery(), CancellationToken.None);

        // Assert
        response.Status.Should().Be("none");
        response.MaskedKey.Should().BeNull();
    }

    [Fact]
    public async Task Should_ReturnActiveWithMaskOnly_When_KeyPresent()
    {
        // Arrange
        const string fullKey = "sk-ant-api03-abcdefghijklmnopB7fA";
        _store.Get().Returns(fullKey);
        var sut = CreateSut();

        // Act
        ApiKeyStatusResponse response = await sut.Handle(new GetApiKeyStatusQuery(), CancellationToken.None);

        // Assert
        response.Status.Should().Be("active");
        response.MaskedKey.Should().Be("sk-ant-api03-…B7fA");
        response.MaskedKey.Should().NotBe(fullKey);
    }
}

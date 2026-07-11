using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Settings.Features.DeleteApiKey;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Settings;

/// <summary>
/// Unit tests for <see cref="DeleteApiKeyCommandHandler"/> (US-02 AC-4). Removal is idempotent — it
/// succeeds whether or not a key was present, and always asks the store to remove.
/// </summary>
public sealed class DeleteApiKeyCommandHandlerTests
{
    private readonly IApiKeyStore _store = Substitute.For<IApiKeyStore>();

    private DeleteApiKeyCommandHandler CreateSut()
    {
        return new DeleteApiKeyCommandHandler(_store);
    }

    [Fact]
    public async Task Should_RemoveKey_And_Succeed_When_Present()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Result result = await sut.Handle(new DeleteApiKeyCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _store.Received(1).Remove();
    }

    [Fact]
    public async Task Should_Succeed_When_NoKeyPresent()
    {
        // Arrange — Remove is a no-op when absent; the handler still succeeds (idempotent).
        var sut = CreateSut();

        // Act
        Result first = await sut.Handle(new DeleteApiKeyCommand(), CancellationToken.None);
        Result second = await sut.Handle(new DeleteApiKeyCommand(), CancellationToken.None);

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        _store.Received(2).Remove();
    }
}

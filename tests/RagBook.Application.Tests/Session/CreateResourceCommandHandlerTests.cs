using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Session.Domain;
using RagBook.Modules.Session.Errors;
using RagBook.Modules.Session.Features.CreateResource;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Session;

public sealed class CreateResourceCommandHandlerTests
{
    private readonly ISessionResourceRepository _repository = Substitute.For<ISessionResourceRepository>();

    private CreateResourceCommandHandler CreateSut()
    {
        return new CreateResourceCommandHandler(_repository);
    }

    [Fact]
    public async Task Should_PersistAndReturnId_When_NameIsValid()
    {
        // Arrange
        var sut = CreateSut();
        var command = new CreateResourceCommand("first resource");

        // Act
        Result<Guid> result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        await _repository.Received(1).AddAsync(
            Arg.Is<SessionResource>(resource => resource.Id == result.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotSetSessionByHand_When_Creating()
    {
        // Arrange
        var sut = CreateSut();
        var command = new CreateResourceCommand("first resource");

        // Act
        await sut.Handle(command, CancellationToken.None);

        // Assert — the handler leaves UserSessionId unset; stamping is the interceptor's job.
        await _repository.Received(1).AddAsync(
            Arg.Is<SessionResource>(resource => resource.UserSessionId == Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnNameRequired_When_NameIsBlank()
    {
        // Arrange
        var sut = CreateSut();
        var command = new CreateResourceCommand("   ");

        // Act
        Result<Guid> result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SessionErrors.NameRequired);
        await _repository.DidNotReceive().AddAsync(Arg.Any<SessionResource>(), Arg.Any<CancellationToken>());
    }
}

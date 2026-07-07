using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Session.Domain;
using RagBook.Modules.Session.Errors;
using RagBook.Modules.Session.Features;
using RagBook.Modules.Session.Features.GetResource;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Session;

public sealed class GetResourceQueryHandlerTests
{
    private readonly ISessionResourceRepository _repository = Substitute.For<ISessionResourceRepository>();

    private GetResourceQueryHandler CreateSut()
    {
        return new GetResourceQueryHandler(_repository);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_RepositoryReturnsNull()
    {
        // Arrange — a null result stands for both "absent" and "owned by another session".
        var sut = CreateSut();
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((SessionResource?)null);

        // Act
        Result<ResourceResponse> result = await sut.Handle(new GetResourceQuery(id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SessionErrors.ResourceNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Should_ReturnResource_When_Found()
    {
        // Arrange
        var sut = CreateSut();
        SessionResource resource = SessionResource.Create("found").Value;
        _repository.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        // Act
        Result<ResourceResponse> result = await sut.Handle(new GetResourceQuery(resource.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(resource.Id);
        result.Value.Name.Should().Be("found");
    }
}

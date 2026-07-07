using FluentAssertions;
using NSubstitute;
using RagBook.Modules.Session.Domain;
using RagBook.Modules.Session.Features;
using RagBook.Modules.Session.Features.ListResources;
using Xunit;

namespace RagBook.Application.Tests.Session;

public sealed class ListResourcesQueryHandlerTests
{
    private readonly ISessionResourceRepository _repository = Substitute.For<ISessionResourceRepository>();

    private ListResourcesQueryHandler CreateSut()
    {
        return new ListResourcesQueryHandler(_repository);
    }

    [Fact]
    public async Task Should_MapRepositoryResults_When_Listing()
    {
        // Arrange
        var sut = CreateSut();
        SessionResource first = SessionResource.Create("a").Value;
        SessionResource second = SessionResource.Create("b").Value;
        _repository.ListAsync(Arg.Any<CancellationToken>()).Returns([first, second]);

        // Act
        IReadOnlyList<ResourceResponse> result = await sut.Handle(new ListResourcesQuery(), CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(response => response.Name).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Should_ReturnEmpty_When_SessionHasNoResources()
    {
        // Arrange
        var sut = CreateSut();
        _repository.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<SessionResource>());

        // Act
        IReadOnlyList<ResourceResponse> result = await sut.Handle(new ListResourcesQuery(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}

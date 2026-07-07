using FluentAssertions;
using RagBook.Modules.Session.Domain;
using RagBook.Modules.Session.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Domain.Tests.Session;

public sealed class SessionResourceTests
{
    [Fact]
    public void Should_GenerateVersion4Guid_When_SessionResourceCreated()
    {
        // Arrange
        const string name = "notes";

        // Act
        Result<SessionResource> result = SessionResource.Create(name);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Version.Should().Be(4);
    }

    [Fact]
    public void Should_TrimName_When_Created()
    {
        // Arrange
        const string name = "  spaced  ";

        // Act
        Result<SessionResource> result = SessionResource.Create(name);

        // Assert
        result.Value.Name.Should().Be("spaced");
    }

    [Fact]
    public void Should_NotStampSession_When_Created()
    {
        // Arrange
        const string name = "notes";

        // Act
        Result<SessionResource> result = SessionResource.Create(name);

        // Assert — the owning session is stamped centrally on save, never by the aggregate.
        result.Value.UserSessionId.Should().Be(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ReturnNameRequired_When_NameIsBlank(string blank)
    {
        // Arrange & Act
        Result<SessionResource> result = SessionResource.Create(blank);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SessionErrors.NameRequired);
    }
}

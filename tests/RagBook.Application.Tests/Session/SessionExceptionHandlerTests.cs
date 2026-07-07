using FluentAssertions;
using RagBook.Modules.Session.Errors;
using RagBook.Shared.Persistence;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Application.Tests.Session;

public sealed class SessionExceptionHandlerTests
{
    [Fact]
    public void Should_MapUniqueViolation_ToResourceAlreadyExists()
    {
        // Arrange & Act
        var mapped = SessionExceptionHandler.TryMap(PersistenceErrorKind.UniqueViolation, out Error error);

        // Assert
        mapped.Should().BeTrue();
        error.Should().Be(SessionErrors.ResourceAlreadyExists);
    }

    [Fact]
    public void Should_MapConcurrencyConflict_ToConcurrencyError()
    {
        // Arrange & Act
        var mapped = SessionExceptionHandler.TryMap(PersistenceErrorKind.ConcurrencyConflict, out Error error);

        // Assert
        mapped.Should().BeTrue();
        error.Should().Be(SessionErrors.ConcurrencyConflict);
    }

    [Theory]
    [InlineData(PersistenceErrorKind.Unknown)]
    [InlineData(PersistenceErrorKind.ForeignKeyViolation)]
    public void Should_NotMap_When_KindIsUnhandled(PersistenceErrorKind kind)
    {
        // Arrange & Act
        var mapped = SessionExceptionHandler.TryMap(kind, out Error error);

        // Assert — unmapped faults fall through to the global exception mapper.
        mapped.Should().BeFalse();
        error.Should().Be(Error.None);
    }
}

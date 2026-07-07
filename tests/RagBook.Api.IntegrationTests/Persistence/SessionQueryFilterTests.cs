using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Session.Domain;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Persistence;

/// <summary>
/// Proves AC-4 architecturally without a live database: every <see cref="ISessionOwned"/> entity in
/// the model carries a global query filter, so no handler can issue an unfiltered read. The model is
/// built offline from the Npgsql provider conventions — no connection is opened.
/// </summary>
public sealed class SessionQueryFilterTests
{
    private sealed class StubSessionContext(Guid sessionId) : ISessionContext
    {
        public Guid UserSessionId => sessionId;
    }

    private static RagBookDbContext CreateContext(Guid sessionId)
    {
        var options = new DbContextOptionsBuilder<RagBookDbContext>()
            .UseNpgsql("Host=localhost;Database=ragbook;Username=postgres;Password=postgres")
            .Options;

        return new RagBookDbContext(options, new StubSessionContext(sessionId));
    }

    [Fact]
    public void Should_ApplyGlobalQueryFilter_To_EverySessionOwnedEntity()
    {
        // Arrange
        using var context = CreateContext(Guid.NewGuid());

        // Act
        var sessionOwnedEntities = context.Model.GetEntityTypes()
            .Where(entityType => typeof(ISessionOwned).IsAssignableFrom(entityType.ClrType))
            .ToList();

        // Assert — there is at least one such entity, and none is left without a query filter.
        sessionOwnedEntities.Should().NotBeEmpty();
        sessionOwnedEntities.Should().OnlyContain(entityType => entityType.GetDeclaredQueryFilters().Any());
    }

    [Fact]
    public void Should_IndexSessionOwnedEntity_By_UserSessionId()
    {
        // Arrange
        using var context = CreateContext(Guid.NewGuid());

        // Act
        var entityType = context.Model.FindEntityType(typeof(SessionResource))!;
        var hasSessionIndex = entityType.GetIndexes()
            .Any(index => index.Properties.Any(property => property.Name == nameof(ISessionOwned.UserSessionId)));

        // Assert
        hasSessionIndex.Should().BeTrue();
    }
}

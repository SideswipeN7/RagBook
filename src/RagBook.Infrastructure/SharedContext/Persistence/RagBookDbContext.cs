using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Session.Domain;
using RagBook.Shared.Sessions;

// Chunk is referenced via the DbSet below.

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// The application's EF Core context. It applies a global query filter on <see cref="ISessionOwned"/>
/// for every such entity, keyed to the injected <see cref="ISessionContext"/> — the single
/// architectural mechanism that makes cross-session reads impossible by construction (AC-4).
/// </summary>
public sealed class RagBookDbContext(DbContextOptions<RagBookDbContext> options, ISessionContext sessionContext)
    : DbContext(options)
{
    /// <summary>The reference session-owned resources.</summary>
    public DbSet<SessionResource> SessionResources => Set<SessionResource>();

    /// <summary>Session-owned documents; the quota counts and sizes these (US-05).</summary>
    public DbSet<Document> Documents => Set<Document>();

    /// <summary>Session-owned folders forming the document tree (US-09).</summary>
    public DbSet<Folder> Folders => Set<Folder>();

    /// <summary>Indexed chunks of documents with their embedding vectors (US-06).</summary>
    public DbSet<Chunk> Chunks => Set<Chunk>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RagBookDbContext).Assembly);

        ApplySessionQueryFilters(modelBuilder);
    }

    private void ApplySessionQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISessionOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(ISessionOwned.UserSessionId));

            // Capture 'this.sessionContext.UserSessionId' so EF re-evaluates the current session per query.
            Expression<Func<Guid>> currentSession = () => sessionContext.UserSessionId;
            var predicate = Expression.Equal(property, currentSession.Body);
            var lambda = Expression.Lambda(predicate, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.Migrations;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the model and scaffold migrations without
/// starting the application host. The connection string here is never used to connect — it only
/// satisfies the Npgsql provider at design time.
/// </summary>
public sealed class RagBookDbContextFactory : IDesignTimeDbContextFactory<RagBookDbContext>
{
    /// <inheritdoc />
    public RagBookDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RagBookDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=ragbook;Username=postgres;Password=postgres",
                npgsql => npgsql.MigrationsAssembly(typeof(RagBookDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new RagBookDbContext(options, new DesignTimeSessionContext());
    }

    private sealed class DesignTimeSessionContext : ISessionContext
    {
        public Guid UserSessionId => Guid.Empty;
    }
}

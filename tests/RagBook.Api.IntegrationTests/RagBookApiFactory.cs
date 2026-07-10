using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Infrastructure.SharedContext.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RagBook.Api.IntegrationTests;

/// <summary>
/// Boots the real API host against a Dockerized PostgreSQL (pgvector) via Testcontainers, applying
/// the EF Core migrations in fixture setup — never at app startup (constitution §VIII). Requires a
/// running Docker engine.
/// </summary>
public sealed class RagBookApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("ragbookdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>Per-factory blob root so uploaded files are isolated and cleaned up (US-04).</summary>
    public string BlobRoot { get; } = Path.Combine(Path.GetTempPath(), "ragbook-blobs", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ragbookdb", _container.GetConnectionString());
        builder.UseSetting("FileStorage:RootPath", BlobRoot);
    }

    /// <summary>Counts stored blob files under the given session's namespace (for orphan-cleanup assertions).</summary>
    public int CountStoredBlobs(Guid sessionId)
    {
        string sessionDir = Path.Combine(BlobRoot, sessionId.ToString("N"));

        return Directory.Exists(sessionDir) ? Directory.GetFiles(sessionDir).Length : 0;
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _container.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();

        if (Directory.Exists(BlobRoot))
        {
            Directory.Delete(BlobRoot, recursive: true);
        }
    }
}

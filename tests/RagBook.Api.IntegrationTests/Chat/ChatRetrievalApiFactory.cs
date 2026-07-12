using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Documents.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Host for the empty-scope test: identical to the standard factory but replaces <see cref="IEmbeddingProvider"/>
/// with a <see cref="CountingEmbeddingProvider"/> (exposed for assertions), so the test can prove the
/// retriever embeds the question only when the scope is non-empty. The swap is applied on this factory's
/// own host via <c>ConfigureTestServices</c>.
/// </summary>
public sealed class ChatRetrievalApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("ragbookdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>The counting embedding provider whose call count the test inspects.</summary>
    public CountingEmbeddingProvider Embeddings { get; private set; } = null!;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ragbookdb", _container.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<CountingEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider>(provider => provider.GetRequiredService<CountingEmbeddingProvider>());
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _container.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();
        await dbContext.Database.MigrateAsync();

        Embeddings = (CountingEmbeddingProvider)Services.GetRequiredService<IEmbeddingProvider>();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Dispose the host (closing pooled DB connections) BEFORE the container, so nothing queries a
        // gone database during teardown.
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}

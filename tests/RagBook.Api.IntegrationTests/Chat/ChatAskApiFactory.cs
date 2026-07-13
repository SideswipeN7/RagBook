using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RagBook.Api.IntegrationTests.Chat.Fakes;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Settings.Domain;
using RagBook.Shared.Sessions;
using Testcontainers.PostgreSql;
using Xunit;

namespace RagBook.Api.IntegrationTests.Chat;

/// <summary>
/// Host for the US-14 ask endpoint: the real pipeline + pgvector retrieval, with the answer generator swapped
/// for the scriptable <see cref="FakeStreamingAnswerGenerator"/> (no real Anthropic). Exposes a helper to
/// store a session key so the pre-generation guard passes. The swap is applied on this factory's own host.
/// </summary>
public sealed class ChatAskApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("ragbookdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>The scriptable generator the endpoint streams from.</summary>
    public FakeStreamingAnswerGenerator Generator { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ragbookdb", _container.GetConnectionString());
        // A short heartbeat so the US-15 keep-alive test can observe a comment on a delaying stream; fast
        // tests complete well under a second, so no heartbeat fires for them.
        builder.UseSetting("Rag:StreamHeartbeatSeconds", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAnswerGenerator>();
            services.AddSingleton<IAnswerGenerator>(Generator);
        });
    }

    /// <summary>Stores a BYOK key for <paramref name="sessionId"/> so the ask endpoint's key guard passes.</summary>
    public void StoreKey(Guid sessionId, string apiKey = "sk-ant-api03-testkeyvalue")
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISessionInitializer>().Initialize(sessionId);
        scope.ServiceProvider.GetRequiredService<IApiKeyStore>().Set(apiKey);
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
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}

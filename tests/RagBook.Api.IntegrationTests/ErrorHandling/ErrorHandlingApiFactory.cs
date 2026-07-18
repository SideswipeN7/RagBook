using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RagBook.Infrastructure.SharedContext.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RagBook.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// Host for the US-19 error-handling tests: the real API with the test-only <c>GET /api/_test/throw</c> endpoint
/// enabled and a <see cref="CapturingLoggerProvider"/> attached, so a test can assert the correlation id appears in
/// both the response and the server logs. Migrations run in fixture setup (never at app startup — §VIII).
/// </summary>
public sealed class ErrorHandlingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("ragbookdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>Captures the server logs for correlation-id assertions.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ragbookdb", _container.GetConnectionString());
        builder.UseSetting("Wolverine:DurabilityEnabled", "false");
        builder.UseSetting("Testing:ExposeThrowEndpoint", "true");

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    public HttpClient CreateSessionClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={Guid.NewGuid()}");

        return client;
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

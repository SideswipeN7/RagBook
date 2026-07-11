using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RagBook.Api.IntegrationTests.Settings.Fakes;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Settings.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace RagBook.Api.IntegrationTests.Settings;

/// <summary>
/// Boots the real API host for the Settings (BYOK) endpoints, swapping the Anthropic validator for an
/// in-memory fake so no test hits the provider (constitution §V). A capturing logger provider lets the
/// no-leak test scan every log line. The swap is applied via <c>ConfigureTestServices</c> on the single
/// host this factory builds (never a derived <c>WithWebHostBuilder</c> host), so Wolverine code
/// generation runs once, normally.
/// </summary>
public sealed class SettingsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("ragbookdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>The fake validator whose next outcome tests control.</summary>
    public MutableFakeApiKeyValidator Validator { get; } = new();

    /// <summary>Captures all log output for the no-leak assertion.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ragbookdb", _container.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IApiKeyValidator>();
            services.AddSingleton<IApiKeyValidator>(Validator);
        });

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
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
    }
}

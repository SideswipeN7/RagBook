using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RagBook.Api.IntegrationTests.Chat.Fakes;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Modules.Chat.Domain;
using RagBook.Modules.Demo.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace RagBook.Api.IntegrationTests.Demo;

/// <summary>
/// Host for the US-03 demo mode: the real chat pipeline + pgvector retrieval, an application key configured (so
/// demo generation is available), a seeded 2-document demo manifest, and the answer generator swapped for the
/// scriptable <see cref="FakeStreamingAnswerGenerator"/> (no real Anthropic). Migrations + demo seeding run in
/// fixture setup (never at app startup — constitution §VIII; durability is off in tests). Subclasses override the
/// demo limits to drive the per-session vs per-IP paths.
/// </summary>
public class DemoApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid DemoDocA = new("d3d00000-0000-4000-8000-0000000000a1");
    public static readonly Guid DemoDocB = new("d3d00000-0000-4000-8000-0000000000b2");

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("ragbookdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>The scriptable generator the endpoint streams from (records the grounded context).</summary>
    public FakeStreamingAnswerGenerator Generator { get; } = new();

    /// <summary>Per-session demo question limit for this factory.</summary>
    protected virtual int MaxQuestionsPerSession => 3;

    /// <summary>Per-IP hourly demo request limit for this factory.</summary>
    protected virtual int MaxQuestionsPerIpPerHour => 50;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ragbookdb", _container.GetConnectionString());
        builder.UseSetting("Wolverine:DurabilityEnabled", "false");

        // The deterministic stand-in embedding can't separate topics; a low threshold lets any seeded demo chunk
        // ground a demo answer deterministically (the point here is the keyless demo path, not retrieval quality).
        builder.UseSetting("Rag:SimilarityThreshold", "0.1");

        // Demo generation is configured; the key is never exposed (only used server-side).
        builder.UseSetting("Anthropic:ApplicationKey", "sk-ant-app-demo-key");

        // Two seeded demo documents (fixed ids) + the demo limits.
        builder.UseSetting("Demo:MaxQuestionsPerSession", MaxQuestionsPerSession.ToString());
        builder.UseSetting("Demo:MaxQuestionsPerIpPerHour", MaxQuestionsPerIpPerHour.ToString());
        builder.UseSetting("Demo:Documents:0:Id", DemoDocA.ToString());
        builder.UseSetting("Demo:Documents:0:FileName", "demo-umowa.txt");
        builder.UseSetting("Demo:Documents:0:ContentType", "text/plain");
        builder.UseSetting("Demo:Documents:0:Text", "Przykladowa umowa demo. Okres wypowiedzenia wynosi trzy miesiace. " + new string('x', 400));
        builder.UseSetting("Demo:Documents:1:Id", DemoDocB.ToString());
        builder.UseSetting("Demo:Documents:1:FileName", "demo-dokumentacja.txt");
        builder.UseSetting("Demo:Documents:1:ContentType", "text/plain");
        builder.UseSetting("Demo:Documents:1:Text", "Dokumentacja techniczna demo. System uzywa pgvector do wyszukiwania. " + new string('y', 400));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAnswerGenerator>();
            services.AddSingleton<IAnswerGenerator>(Generator);
        });
    }

    /// <summary>Seeds the demo documents (idempotent). Returns the seeder so a test can call it twice.</summary>
    public async Task<int> SeedAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDemoDocumentSeeder>().SeedAsync(CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<RagBookDbContext>();

        return await dbContext.Documents
            .IgnoreQueryFilters()
            .CountAsync(document => document.Origin == RagBook.Modules.Documents.Domain.DocumentOrigin.Demo);
    }

    public HttpClient CreateSessionClient(Guid sessionId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

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

/// <summary>A demo factory with a tiny per-IP limit (and a large per-session limit) to drive the AC-3 path.</summary>
public sealed class DemoIpLimitApiFactory : DemoApiFactory
{
    protected override int MaxQuestionsPerSession => 100;

    protected override int MaxQuestionsPerIpPerHour => 2;
}

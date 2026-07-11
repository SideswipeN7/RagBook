using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Infrastructure.SharedContext.Interceptors;
using RagBook.Infrastructure.SharedContext.Persistence;
using RagBook.Infrastructure.SharedContext.Providers.Anthropic;
using RagBook.Infrastructure.SharedContext.Sessions;
using RagBook.Infrastructure.SharedContext.Settings;
using RagBook.Infrastructure.SharedContext.Storage;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Folders.Domain;
using RagBook.Modules.Session.Domain;
using RagBook.Modules.Settings.Domain;
using RagBook.Modules.Tree.Domain;
using RagBook.Shared.Persistence;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure;

/// <summary>Composition root for the infrastructure layer.</summary>
public static class DependencyInjection
{
    /// <summary>Assembly that holds EF Core migrations (constitution §VIII — migrations project only).</summary>
    public const string MigrationsAssemblyName = "RagBook.Infrastructure.Migrations";

    /// <summary>
    /// Registers persistence, the session context, interceptors, and repositories against the given
    /// PostgreSQL <paramref name="connectionString"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingletonTimeProvider();

        // One SessionContext instance per request backs both the reader and the initializer.
        services.AddScoped<SessionContext>();
        services.AddScoped<ISessionContext>(provider => provider.GetRequiredService<SessionContext>());
        services.AddScoped<ISessionInitializer>(provider => provider.GetRequiredService<SessionContext>());

        services.AddSingleton<IPersistenceExceptionClassifier, NpgsqlPersistenceExceptionClassifier>();

        services.AddScoped<SessionStampingInterceptor>();
        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<RagBookDbContext>((provider, options) =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(MigrationsAssemblyName));
            options.AddInterceptors(
                provider.GetRequiredService<SessionStampingInterceptor>(),
                provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddScoped<ISessionResourceRepository, SessionResourceRepository>();
        services.AddScoped<IDocumentQuotaRepository, DocumentQuotaRepository>();

        services.AddScoped<IFolderRepository, FolderRepository>();

        // US-04 upload wiring. The real folder file-probe REPLACES US-09's NoFolderFilesProbe, so
        // deleting a folder that contains documents is now blocked (US-09 AC-5 closed end-to-end).
        services.AddScoped<IFolderFileProbe, DocumentFolderFileProbe>();
        services.AddScoped<IFolderReference, FolderReference>();
        services.AddScoped<IDocumentUploadRepository, DocumentUploadRepository>();
        services.AddScoped<IFileStorage, LocalFileStorage>();

        // US-07 tree read — one seam composing folders + documents in two session-scoped queries.
        services.AddScoped<ITreeReader, TreeReader>();

        // US-02 BYOK — the session-scoped API key lives only in memory (constitution §VII), never in
        // the database. The validator reaches Anthropic through a resilient named HttpClient; the client
        // factory guards generation with settings.api_key_missing when no key is present.
        services.AddMemoryCache();
        services.AddScoped<IApiKeyStore, MemoryCacheApiKeyStore>();
        services.AddScoped<IApiKeyThrottle, MemoryCacheApiKeyThrottle>();
        services.AddScoped<IApiKeyValidator, AnthropicApiKeyValidator>();
        services.AddScoped<IAnthropicClientFactory, AnthropicClientFactory>();

        services.AddHttpClient(AnthropicApiKeyValidator.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                AnthropicOptions anthropic = provider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<AnthropicOptions>>().Value;
                client.BaseAddress = new Uri(anthropic.BaseUrl);
            })
            .AddStandardResilienceHandler();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}

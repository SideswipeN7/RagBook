using RagBook;
using RagBook.API.Endpoints;
using RagBook.API.ProblemDetails;
using RagBook.API.Sessions;
using RagBook.Infrastructure;
using JasperFx.CodeGeneration.Model;
using JasperFx.Resources;
using RagBook.API.Messaging;
using RagBook.Infrastructure.SharedContext.Providers.Anthropic;
using RagBook.Infrastructure.SharedContext.Processing;
using RagBook.Infrastructure.SharedContext.Storage;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Chat;
using RagBook.Modules.Documents.Processing;
using RagBook.Modules.Documents.Quota;
using RagBook.Modules.Folders;
using RagBook.Modules.Settings;
using RagBook.Shared.Messaging;
using RagBook.ServiceDefaults;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<SessionCookieOptions>(builder.Configuration.GetSection(SessionCookieOptions.SectionName));
builder.Services.Configure<QuotaOptions>(builder.Configuration.GetSection(QuotaOptions.SectionName));
builder.Services.Configure<FolderOptions>(builder.Configuration.GetSection(FolderOptions.SectionName));
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));
builder.Services.Configure<ApiKeyStoreOptions>(builder.Configuration.GetSection(ApiKeyStoreOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection(ChunkingOptions.SectionName));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection(ChatOptions.SectionName));

builder.Services.AddApp();

var connectionString = builder.Configuration.GetConnectionString("ragbookdb")
    ?? throw new InvalidOperationException("Connection string 'ragbookdb' is not configured.");
builder.Services.AddInfrastructure(connectionString);

// US-06 embeddings: the real Voyage driver when a key is configured (Secret Manager), else the
// deterministic stand-in (dev/tests) — one model for the whole index either way.
var embeddingApiKey = builder.Configuration[$"{EmbeddingOptions.SectionName}:ApiKey"];
if (!string.IsNullOrWhiteSpace(embeddingApiKey))
{
    builder.Services.AddHttpClient<IEmbeddingProvider, VoyageEmbeddingProvider>();
}
else
{
    builder.Services.AddScoped<IEmbeddingProvider, FakeEmbeddingProvider>();
}

// The core layer publishes in-process events through IEventPublisher; the host backs it with Wolverine.
builder.Services.AddScoped<IEventPublisher, WolverineEventPublisher>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Durable messaging (US-06 FR-002) so a queued/in-flight processing job survives a restart. Guarded by
// config so integration tests (which invoke the handler directly) run without provisioning envelope
// storage; the running app enables it and sets up the Wolverine tables on startup.
var durabilityEnabled = builder.Configuration.GetValue("Wolverine:DurabilityEnabled", defaultValue: true);

builder.Host.UseWolverine(options =>
{
    options.UseFluentValidation();
    options.Discovery.IncludeAssembly(typeof(Marker).Assembly);

    // Handler dependencies (RagBookDbContext and the scoped ISessionContext behind it) are registered
    // via factory lambdas, so Wolverine cannot construct them inline and must resolve them from the
    // request container. Wolverine 6.x defaults ServiceLocationPolicy to NotAllowed, which otherwise
    // fails handler code generation with InvalidServiceLocationException.
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    if (durabilityEnabled)
    {
        options.PersistMessagesWithPostgresql(connectionString);
    }
});

if (durabilityEnabled)
{
    builder.Services.AddResourceSetupOnStartup();
}

var app = builder.Build();

app.UseExceptionHandler();

// Resolve/issue the session before anything reads domain data (AC-1).
app.UseMiddleware<SessionMiddleware>();

app.MapDefaultEndpoints();
app.MapSessionEndpoints();
app.MapResourceEndpoints();
app.MapQuotaEndpoints();
app.MapFolderEndpoints();
app.MapDocumentEndpoints();
app.MapTreeEndpoints();
app.MapSettingsEndpoints();
app.MapDocumentStatusEndpoints();
app.MapChatEndpoints();

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the integration tests.</summary>
public partial class Program;

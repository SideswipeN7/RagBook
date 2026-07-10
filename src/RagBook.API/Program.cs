using RagBook;
using RagBook.API.Endpoints;
using RagBook.API.ProblemDetails;
using RagBook.API.Sessions;
using RagBook.Infrastructure;
using JasperFx.CodeGeneration.Model;
using RagBook.API.Messaging;
using RagBook.Infrastructure.SharedContext.Storage;
using RagBook.Modules.Documents.Quota;
using RagBook.Modules.Folders;
using RagBook.Shared.Messaging;
using RagBook.ServiceDefaults;
using Wolverine;
using Wolverine.FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<SessionCookieOptions>(builder.Configuration.GetSection(SessionCookieOptions.SectionName));
builder.Services.Configure<QuotaOptions>(builder.Configuration.GetSection(QuotaOptions.SectionName));
builder.Services.Configure<FolderOptions>(builder.Configuration.GetSection(FolderOptions.SectionName));
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));

builder.Services.AddApp();

var connectionString = builder.Configuration.GetConnectionString("ragbookdb")
    ?? throw new InvalidOperationException("Connection string 'ragbookdb' is not configured.");
builder.Services.AddInfrastructure(connectionString);

// The core layer publishes in-process events through IEventPublisher; the host backs it with Wolverine.
builder.Services.AddScoped<IEventPublisher, WolverineEventPublisher>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Host.UseWolverine(options =>
{
    options.UseFluentValidation();
    options.Discovery.IncludeAssembly(typeof(Marker).Assembly);

    // Handler dependencies (RagBookDbContext and the scoped ISessionContext behind it) are registered
    // via factory lambdas, so Wolverine cannot construct them inline and must resolve them from the
    // request container. Wolverine 6.x defaults ServiceLocationPolicy to NotAllowed, which otherwise
    // fails handler code generation with InvalidServiceLocationException.
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
});

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

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the integration tests.</summary>
public partial class Program;

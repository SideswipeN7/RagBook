using RagBook;
using RagBook.API.Endpoints;
using RagBook.API.ProblemDetails;
using RagBook.API.Sessions;
using RagBook.Infrastructure;
using RagBook.ServiceDefaults;
using Wolverine;
using Wolverine.FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<SessionCookieOptions>(builder.Configuration.GetSection(SessionCookieOptions.SectionName));

builder.Services.AddApp();

var connectionString = builder.Configuration.GetConnectionString("ragbookdb")
    ?? throw new InvalidOperationException("Connection string 'ragbookdb' is not configured.");
builder.Services.AddInfrastructure(connectionString);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Host.UseWolverine(options =>
{
    options.UseFluentValidation();
    options.Discovery.IncludeAssembly(typeof(Marker).Assembly);
});

var app = builder.Build();

app.UseExceptionHandler();

// Resolve/issue the session before anything reads domain data (AC-1).
app.UseMiddleware<SessionMiddleware>();

app.MapDefaultEndpoints();
app.MapSessionEndpoints();
app.MapResourceEndpoints();

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c> in the integration tests.</summary>
public partial class Program;

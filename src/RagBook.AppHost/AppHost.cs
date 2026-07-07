var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector, provisioned by Aspire. pgvector is unused in US-01 but the image is
// chosen now so later RAG stories inherit it. Migrations are applied out-of-band, never at startup.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume();

var ragbookdb = postgres.AddDatabase("ragbookdb");

var api = builder.AddProject<Projects.RagBook_API>("api")
    .WithReference(ragbookdb)
    .WaitFor(ragbookdb);

// Angular dev server. Aspire 13.4.6 ships no compatible AddNpmApp — Aspire.Hosting.NodeJs and the
// CommunityToolkit Node hosting are on the incompatible 9.x line — so the app host orchestrates the
// SPA with core AddExecutable running `npm run start` in src/Web. `npm install` in src/Web is a
// prerequisite; the dev server proxies /api to the API (see src/Web/proxy.conf.js).
builder.AddExecutable("web", "npm", "../Web", "run", "start")
    .WithHttpEndpoint(port: 4200, targetPort: 4200, name: "http")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();

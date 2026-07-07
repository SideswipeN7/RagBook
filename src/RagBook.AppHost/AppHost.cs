var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector, provisioned by Aspire. pgvector is unused in US-01 but the image is
// chosen now so later RAG stories inherit it. Migrations are applied out-of-band, never at startup.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume();

var ragbookdb = postgres.AddDatabase("ragbookdb");

builder.AddProject<Projects.RagBook_API>("api")
    .WithReference(ragbookdb)
    .WaitFor(ragbookdb);

builder.Build().Run();

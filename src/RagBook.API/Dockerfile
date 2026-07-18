# syntax=docker/dockerfile:1
# Multi-stage build for the RagBook API (US-20). One build stage publishes the API and produces a self-contained
# EF Core migration bundle; two thin runtime targets ship the API (`api`) and the migration runner (`migrate`).
# Build context = repo root (RagBook.slnx lives there).

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the source, tool manifest, and the root MSBuild props (central TargetFramework + package versions). We build
# only the API's reference graph (RagBook, Infrastructure, Infrastructure.Migrations, ServiceDefaults) — NOT the
# Aspire AppHost, which needs the Aspire workload and is a dev-only orchestrator.
COPY global.json dotnet-tools.json Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/

# Publish the API (restores its graph transitively; the AppHost is never referenced by the API).
RUN dotnet publish src/RagBook.API/RagBook.API.csproj -c Release -o /app/publish /p:UseAppHost=false

# Produce a framework-dependent migration bundle (applied out-of-band as a deploy step — constitution §VIII).
# The Migrations project is self-sufficient for tooling: it holds the migrations + an IDesignTimeDbContextFactory
# (RagBookDbContextFactory), so it is both the --project and the --startup-project (the API does not reference it).
# Run on the `runtime` image below (which provides the shared framework).
RUN dotnet restore src/RagBook.Infrastructure.Migrations/RagBook.Infrastructure.Migrations.csproj \
    && dotnet tool restore \
    && dotnet ef migrations bundle \
        --project src/RagBook.Infrastructure.Migrations/RagBook.Infrastructure.Migrations.csproj \
        --startup-project src/RagBook.Infrastructure.Migrations/RagBook.Infrastructure.Migrations.csproj \
        -o /app/efbundle

# ---- api runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /app/publish ./
# ASP.NET Core listens on 8080 by default in this image; nginx proxies /api here.
EXPOSE 8080
ENTRYPOINT ["dotnet", "RagBook.API.dll"]

# ---- migration runner ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS migrate
WORKDIR /app
COPY --from=build /app/efbundle ./efbundle
# Run as: efbundle --connection "<connection string>" (the compose `migrate` service supplies it).
ENTRYPOINT ["./efbundle"]

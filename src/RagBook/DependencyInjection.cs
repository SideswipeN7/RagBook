using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Quota;

namespace RagBook;

/// <summary>Composition root for the core application layer. Registers all module slices.</summary>
public static class DependencyInjection
{
    /// <summary>Registers application services (validators; handlers are discovered by Wolverine).</summary>
    public static IServiceCollection AddApp(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<Marker>(includeInternalTypes: true);

        // Documents module — quota enforcement (US-05). The repository seam is bound in Infrastructure.
        services.AddScoped<IQuotaService, QuotaService>();

        return services;
    }
}

/// <summary>Assembly marker for scanning the core application layer.</summary>
public sealed class Marker;

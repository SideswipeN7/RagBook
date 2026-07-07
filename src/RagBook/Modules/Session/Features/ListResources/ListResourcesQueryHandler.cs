using RagBook.Modules.Session.Domain;

namespace RagBook.Modules.Session.Features.ListResources;

/// <summary>
/// Handles <see cref="ListResourcesQuery"/>. The repository only ever returns the current session's
/// rows (global query filter), so another session's resources never appear (AC-3).
/// </summary>
public sealed class ListResourcesQueryHandler(ISessionResourceRepository repository)
{
    /// <summary>Returns the current session's resources as read models.</summary>
    public async Task<IReadOnlyList<ResourceResponse>> Handle(ListResourcesQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<SessionResource> resources = await repository.ListAsync(cancellationToken);

        return resources
            .Select(resource => new ResourceResponse(resource.Id, resource.Name, resource.CreatedAt))
            .ToList();
    }
}

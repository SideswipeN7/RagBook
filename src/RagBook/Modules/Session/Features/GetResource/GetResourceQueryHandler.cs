using RagBook.Modules.Session.Domain;
using RagBook.Modules.Session.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Session.Features.GetResource;

/// <summary>
/// Handles <see cref="GetResourceQuery"/>. A resource owned by another session is invisible to the
/// repository (global query filter), so it returns the same <c>session.resource_not_found</c> as an
/// absent one — surfaced by the web layer as 404, never 403 (AC-3).
/// </summary>
public sealed class GetResourceQueryHandler(ISessionResourceRepository repository)
{
    /// <summary>Returns the resource, or a not-found error.</summary>
    public async Task<Result<ResourceResponse>> Handle(GetResourceQuery query, CancellationToken cancellationToken)
    {
        SessionResource? resource = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (resource is null)
        {
            return SessionErrors.ResourceNotFound;
        }

        return new ResourceResponse(resource.Id, resource.Name, resource.CreatedAt);
    }
}

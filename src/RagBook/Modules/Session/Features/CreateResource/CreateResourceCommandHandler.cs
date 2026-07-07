using RagBook.Modules.Session.Domain;
using RagBook.Shared.Results;

namespace RagBook.Modules.Session.Features.CreateResource;

/// <summary>
/// Handles <see cref="CreateResourceCommand"/>. The owning session is stamped centrally on save, so
/// this handler never sets <c>UserSessionId</c> by hand (constitution §III/§VI).
/// </summary>
public sealed class CreateResourceCommandHandler(ISessionResourceRepository repository)
{
    /// <summary>Creates the resource and returns its identity, or a validation error.</summary>
    public async Task<Result<Guid>> Handle(CreateResourceCommand command, CancellationToken cancellationToken)
    {
        Result<SessionResource> creation = SessionResource.Create(command.Name);
        if (creation.IsFailure)
        {
            return Result.Failure<Guid>(creation.Error);
        }

        SessionResource resource = creation.Value;
        await repository.AddAsync(resource, cancellationToken);

        return resource.Id;
    }
}

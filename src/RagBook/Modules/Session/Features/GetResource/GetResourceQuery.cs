using RagBook.Shared.Messaging;
using RagBook.Shared.Results;

namespace RagBook.Modules.Session.Features.GetResource;

/// <summary>Reads a single session-owned resource by id, scoped to the current session.</summary>
/// <param name="Id">Resource identifier.</param>
public sealed record GetResourceQuery(Guid Id) : IQuery<Result<ResourceResponse>>;

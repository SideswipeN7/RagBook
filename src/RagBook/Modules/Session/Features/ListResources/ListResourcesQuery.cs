using RagBook.Shared.Messaging;

namespace RagBook.Modules.Session.Features.ListResources;

/// <summary>Lists the current session's resources.</summary>
public sealed record ListResourcesQuery : IQuery<IReadOnlyList<ResourceResponse>>;

using RagBook.Shared.Messaging;

namespace RagBook.Modules.Session.Features.CreateResource;

/// <summary>Creates a session-owned resource for the current session.</summary>
/// <param name="Name">The resource name.</param>
public sealed record CreateResourceCommand(string Name) : ICommand<Guid>
{
    /// <summary>Maximum accepted length of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 200;
}

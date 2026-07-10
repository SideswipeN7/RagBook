using RagBook.Shared.Messaging;

namespace RagBook.Modules.Folders.Features.CreateFolder;

/// <summary>Creates a session-owned folder, at the root or inside an existing folder (AC-1).</summary>
/// <param name="Name">The folder name (validated and trimmed by the domain).</param>
/// <param name="ParentId">The parent folder id, or <c>null</c> to create a root folder.</param>
public sealed record CreateFolderCommand(string Name, Guid? ParentId) : ICommand<Guid>;

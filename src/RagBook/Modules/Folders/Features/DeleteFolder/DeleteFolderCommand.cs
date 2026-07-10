using RagBook.Shared.Messaging;

namespace RagBook.Modules.Folders.Features.DeleteFolder;

/// <summary>Deletes a session-owned folder, but only when it is empty (AC-5).</summary>
/// <param name="Id">The folder to delete.</param>
public sealed record DeleteFolderCommand(Guid Id) : ICommand;

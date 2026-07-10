using RagBook.Shared.Messaging;

namespace RagBook.Modules.Folders.Features.RenameFolder;

/// <summary>Renames a session-owned folder; changes only the name, never its place (AC-4).</summary>
/// <param name="Id">The folder to rename.</param>
/// <param name="NewName">The new name (validated and trimmed by the domain).</param>
public sealed record RenameFolderCommand(Guid Id, string NewName) : ICommand;

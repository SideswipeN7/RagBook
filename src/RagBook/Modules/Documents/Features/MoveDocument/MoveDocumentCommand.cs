using RagBook.Shared.Messaging;

namespace RagBook.Modules.Documents.Features.MoveDocument;

/// <summary>Moves a document to a target folder, or to the root when <paramref name="TargetFolderId"/> is null (US-10).</summary>
/// <param name="DocumentId">The document to move.</param>
/// <param name="TargetFolderId">The destination folder, or <c>null</c> for the root.</param>
public sealed record MoveDocumentCommand(Guid DocumentId, Guid? TargetFolderId) : ICommand;

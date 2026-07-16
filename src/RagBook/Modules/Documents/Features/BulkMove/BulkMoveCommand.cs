using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Messaging;

namespace RagBook.Modules.Documents.Features.BulkMove;

/// <summary>
/// Moves every document in <paramref name="Ids"/> to one folder (or the root when <paramref name="TargetFolderId"/>
/// is null) with all-or-nothing semantics (US-12): validate the whole set first, then apply in one transaction.
/// </summary>
/// <param name="Ids">The documents to move (de-duplicated server-side).</param>
/// <param name="TargetFolderId">The destination folder, or <c>null</c> for the root.</param>
public sealed record BulkMoveCommand(IReadOnlyList<Guid> Ids, Guid? TargetFolderId) : ICommand<BulkResult>;

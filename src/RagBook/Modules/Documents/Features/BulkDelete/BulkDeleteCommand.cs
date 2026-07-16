using RagBook.Modules.Documents.Domain;
using RagBook.Shared.Messaging;

namespace RagBook.Modules.Documents.Features.BulkDelete;

/// <summary>
/// Deletes every document in <paramref name="Ids"/> with all-or-nothing semantics (US-12): validate the whole set
/// first, then delete the records + their chunks (cascade) in one transaction; the quota drops by the number deleted.
/// </summary>
/// <param name="Ids">The documents to delete (de-duplicated server-side).</param>
public sealed record BulkDeleteCommand(IReadOnlyList<Guid> Ids) : ICommand<BulkResult>;

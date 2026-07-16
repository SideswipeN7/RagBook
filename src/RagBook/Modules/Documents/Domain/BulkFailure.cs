namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// One offending item in an all-or-nothing bulk operation (US-12): the requested id together with the stable
/// reason <paramref name="Code"/> (e.g. <c>document.not_found</c>, <c>document.read_only</c>,
/// <c>folder.not_found</c>). The endpoint surfaces the full list as the <c>failures[]</c> extension of a
/// <c>422</c> ProblemDetails so the frontend can mark exactly the items that blocked the operation.
/// </summary>
/// <param name="Id">The offending id (a document id, or the target folder id for a missing move target).</param>
/// <param name="Code">The stable reason code for why this item failed validation.</param>
public sealed record BulkFailure(Guid Id, string Code);

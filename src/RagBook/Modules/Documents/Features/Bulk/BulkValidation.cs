using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Features.Bulk;

/// <summary>
/// Shared all-or-nothing validation for the bulk slices (US-12): de-duplicate the id list, guard empty / over-cap,
/// and turn the session-filtered read into the per-id <see cref="BulkFailure"/> list (a requested id absent from
/// the session ⇒ <c>document.not_found</c>; a demo document ⇒ <c>document.read_only</c>). Move adds its own
/// target-folder failure on top. Keeping this in one place ensures BulkMove and BulkDelete apply identical rules.
/// </summary>
internal static class BulkValidation
{
    /// <summary>
    /// De-duplicates <paramref name="ids"/> (order-preserving) and rejects an empty or over-<paramref name="maxItems"/>
    /// list. Returns the normalized id list, or a <see cref="BulkResult.BadRequest"/> (empty / over-cap → 400).
    /// </summary>
    public static Result<IReadOnlyList<Guid>> Normalize(IReadOnlyList<Guid> ids, int maxItems)
    {
        IReadOnlyList<Guid> distinct = ids.Distinct().ToList();

        if (distinct.Count == 0)
        {
            return Result.Failure<IReadOnlyList<Guid>>(DocumentErrors.BulkEmpty);
        }

        if (distinct.Count > maxItems)
        {
            return Result.Failure<IReadOnlyList<Guid>>(DocumentErrors.BulkTooLarge);
        }

        return Result.Success(distinct);
    }

    /// <summary>
    /// Builds the per-id failures shared by both operations: any requested id not present in
    /// <paramref name="documents"/> (session-filtered read) is <c>document.not_found</c>; any present demo document
    /// is <c>document.read_only</c>. The order follows <paramref name="requestedIds"/> for a stable response.
    /// </summary>
    public static List<BulkFailure> FindItemFailures(
        IReadOnlyList<Guid> requestedIds,
        IReadOnlyList<Document> documents)
    {
        Dictionary<Guid, Document> byId = documents.ToDictionary(document => document.Id);
        var failures = new List<BulkFailure>();

        foreach (Guid id in requestedIds)
        {
            if (!byId.TryGetValue(id, out Document? document))
            {
                failures.Add(new BulkFailure(id, DocumentErrors.NotFound.Code));
            }
            else if (document.Origin == DocumentOrigin.Demo)
            {
                failures.Add(new BulkFailure(id, DocumentErrors.ReadOnly.Code));
            }
        }

        return failures;
    }
}

using RagBook.Modules.Tree.Domain;

namespace RagBook.Modules.Tree.Features.GetTree;

/// <summary>
/// Handles <see cref="GetTreeQuery"/>. Reads the session's folders + documents through the single
/// <see cref="ITreeReader"/> seam (two session-scoped, pre-ordered queries — no N+1) and returns them as
/// one <see cref="TreeResponse"/>. A pure read; ordering is done by the reader, so this is a pass-through.
/// </summary>
public sealed class GetTreeQueryHandler(ITreeReader treeReader)
{
    /// <summary>Returns the composed tree data for the current session.</summary>
    public async Task<TreeResponse> Handle(GetTreeQuery query, CancellationToken cancellationToken)
    {
        TreeData data = await treeReader.GetAsync(cancellationToken);

        return new TreeResponse(data.Folders, data.Documents);
    }
}

using Pgvector;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// One indexed slice of a document (US-06): its position, text, optional source page (for citations),
/// and embedding vector. Belongs to exactly one <see cref="Document"/> (cascade-deleted with it) and is
/// unique per <c>(DocumentId, Index)</c>. <see cref="ISessionOwned"/> so the global query filter isolates
/// it; the background worker stamps the owning session before insert. Re-indexing replaces the whole set,
/// so a chunk is immutable after creation.
/// </summary>
public sealed class Chunk : ISessionOwned
{
    private Chunk(Guid id, Guid documentId, int index, string text, int? pageNumber, Vector embedding)
    {
        Id = id;
        DocumentId = documentId;
        Index = index;
        Text = text;
        PageNumber = pageNumber;
        Embedding = embedding;
    }

    // Required by EF Core for materialization.
    private Chunk()
    {
        Text = string.Empty;
        Embedding = new Vector(ReadOnlyMemory<float>.Empty);
    }

    /// <summary>Identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>Owning document; FK cascade-deletes the chunk when the document is removed (US-08).</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>0-based position within the document; unique per <c>(DocumentId, Index)</c>.</summary>
    public int Index { get; private set; }

    /// <summary>The chunk text (normalized).</summary>
    public string Text { get; private set; }

    /// <summary>Source page (PDF) for citations; <c>null</c> for TXT/MD.</summary>
    public int? PageNumber { get; private set; }

    /// <summary>The embedding vector (fixed configured dimension), stored as a pgvector value.</summary>
    public Vector Embedding { get; private set; }

    /// <inheritdoc />
    public Guid UserSessionId { get; private set; }

    /// <summary>Creates a chunk for <paramref name="documentId"/> at <paramref name="index"/>.</summary>
    public static Chunk Create(Guid documentId, int index, string text, int? pageNumber, float[] embedding)
    {
        return new Chunk(Guid.NewGuid(), documentId, index, text, pageNumber, new Vector(embedding));
    }
}

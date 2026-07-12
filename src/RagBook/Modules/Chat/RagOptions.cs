namespace RagBook.Modules.Chat;

/// <summary>
/// Config-driven RAG retrieval parameters (constitution §V — no magic numbers). Bound from the <c>Rag</c>
/// section. US-13 needs only <see cref="TopK"/>; the similarity threshold and grounding sentinel are
/// added by US-14/US-17.
/// </summary>
public sealed class RagOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Rag";

    /// <summary>Maximum number of passages a retrieval returns (the vector-search <c>LIMIT</c>).</summary>
    public int TopK { get; set; } = 8;
}

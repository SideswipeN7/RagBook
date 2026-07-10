using RagBook.Modules.Folders.Domain;

namespace RagBook.Modules.Folders;

/// <summary>
/// Configuration for the folder tree. Bound from the <c>Folders</c> section so every limit is
/// config-driven — no magic numbers (constitution §VII).
/// </summary>
public sealed class FolderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Folders";

    /// <summary>Maximum nesting depth; root folders are depth 1 (AC-2).</summary>
    public int MaxDepth { get; set; } = 3;

    /// <summary>Maximum length of a trimmed folder name (AC-6).</summary>
    public int MaxNameLength { get; set; } = 100;

    /// <summary>Projects the configured limits into the domain's <see cref="FolderNameRules"/>.</summary>
    public FolderNameRules ToNameRules()
    {
        return new FolderNameRules(MaxNameLength);
    }
}

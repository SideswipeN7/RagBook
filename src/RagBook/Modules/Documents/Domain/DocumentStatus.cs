namespace RagBook.Modules.Documents.Domain;

/// <summary>
/// Lifecycle state of a <see cref="Document"/>. Minimal in US-05 (quota); US-06 (background
/// processing) drives the transitions. A <see cref="Failed"/> document still counts toward the quota
/// until the user deletes it (US-05 decision).
/// </summary>
public enum DocumentStatus
{
    /// <summary>Freshly created; awaiting or undergoing processing.</summary>
    Processing = 0,

    /// <summary>Successfully processed and indexed.</summary>
    Ready = 1,

    /// <summary>Processing failed; still counts toward quota until removed.</summary>
    Failed = 2,
}

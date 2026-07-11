namespace RagBook.Modules.Settings.Domain;

/// <summary>The session's state with respect to a stored generation key (US-02 FR-007).</summary>
public enum ApiKeyStatus
{
    /// <summary>No key is stored for the session.</summary>
    None = 0,

    /// <summary>A validated key is stored for the session.</summary>
    Active = 1,
}

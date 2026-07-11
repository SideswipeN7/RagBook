namespace RagBook.Modules.Settings.Domain;

/// <summary>Outcome of validating a candidate key against the provider (US-02 AC-1).</summary>
public enum ApiKeyValidationResult
{
    /// <summary>The provider accepted the key.</summary>
    Valid = 0,

    /// <summary>The provider refused the key (invalid, revoked, no credit).</summary>
    Rejected = 1,

    /// <summary>The provider could not be reached to decide (timeout / 5xx / network). Transient.</summary>
    Unavailable = 2,
}

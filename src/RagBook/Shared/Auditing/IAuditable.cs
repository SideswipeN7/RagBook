namespace RagBook.Shared.Auditing;

/// <summary>
/// Marks an entity whose audit fields are stamped centrally by an EF Core interceptor using
/// <see cref="TimeProvider"/> — never by hand in handlers (constitution §VI).
/// </summary>
public interface IAuditable
{
    /// <summary>UTC instant the entity was created.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Actor that created the entity (session id, or <c>system</c> for background work).</summary>
    string CreatedBy { get; set; }

    /// <summary>UTC instant the entity was last modified, if ever.</summary>
    DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Actor that last modified the entity, if ever.</summary>
    string? ModifiedBy { get; set; }
}

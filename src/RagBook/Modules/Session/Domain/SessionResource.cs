using RagBook.Modules.Session.Errors;
using RagBook.Shared.Auditing;
using RagBook.Shared.Results;
using RagBook.Shared.Sessions;

namespace RagBook.Modules.Session.Domain;

/// <summary>
/// The foundation reference session-owned resource. It is the copy-me template for future module
/// aggregates and the subject the isolation acceptance tests exercise. <see cref="UserSessionId"/>
/// is stamped centrally on insert (never in handlers); isolation is enforced at the query boundary,
/// not by this aggregate.
/// </summary>
public sealed class SessionResource : ISessionOwned, IAuditable
{
    private SessionResource(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    // Required by EF Core for materialization.
    private SessionResource()
    {
        Name = string.Empty;
    }

    /// <summary>Identity (GUID v4).</summary>
    public Guid Id { get; private set; }

    /// <summary>Display name.</summary>
    public string Name { get; private set; }

    /// <inheritdoc />
    public Guid UserSessionId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Creates a new resource, enforcing the name invariant. Returns a failed result rather than
    /// throwing for the expected "name missing" case (constitution §IV).
    /// </summary>
    public static Result<SessionResource> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SessionErrors.NameRequired;
        }

        return new SessionResource(Guid.NewGuid(), name.Trim());
    }
}

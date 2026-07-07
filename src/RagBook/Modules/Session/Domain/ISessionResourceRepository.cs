namespace RagBook.Modules.Session.Domain;

/// <summary>
/// Persistence seam for <see cref="SessionResource"/>. Every read is automatically constrained to
/// the current session by the EF Core global query filter behind the implementation (AC-4) — this
/// interface exposes no way to query across sessions.
/// </summary>
public interface ISessionResourceRepository
{
    /// <summary>Persists a new resource; the owning session is stamped centrally on save.</summary>
    Task AddAsync(SessionResource resource, CancellationToken cancellationToken);

    /// <summary>Returns the resource by id, or <c>null</c> when it is absent or owned by another session.</summary>
    Task<SessionResource?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns the current session's resources.</summary>
    Task<IReadOnlyList<SessionResource>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Counts the current session's resources.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);
}

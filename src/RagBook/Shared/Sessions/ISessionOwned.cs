namespace RagBook.Shared.Sessions;

/// <summary>
/// Marks an entity as owned by a single session. Every implementer automatically gains the EF Core
/// global query filter on <see cref="UserSessionId"/> and central stamping on insert, so a handler
/// can never read or create a row for another session (constitution §III, AC-4).
/// </summary>
public interface ISessionOwned
{
    /// <summary>Owning session identifier. Stamped centrally on insert — never set by hand.</summary>
    Guid UserSessionId { get; }
}

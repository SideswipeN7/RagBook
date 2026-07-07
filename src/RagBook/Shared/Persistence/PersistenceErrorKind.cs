namespace RagBook.Shared.Persistence;

/// <summary>
/// Provider-agnostic classification of an infrastructure persistence failure, produced by
/// <see cref="IPersistenceExceptionClassifier"/> so module exception handlers can map it to a
/// domain error code without referencing the database provider.
/// </summary>
public enum PersistenceErrorKind
{
    /// <summary>Not a recognised, expected persistence failure.</summary>
    Unknown = 0,

    /// <summary>A unique-constraint violation (e.g. Postgres SQLSTATE 23505).</summary>
    UniqueViolation = 1,

    /// <summary>A foreign-key violation (e.g. Postgres SQLSTATE 23503).</summary>
    ForeignKeyViolation = 2,

    /// <summary>An optimistic-concurrency conflict.</summary>
    ConcurrencyConflict = 3,
}

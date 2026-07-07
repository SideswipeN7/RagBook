using Microsoft.EntityFrameworkCore;
using Npgsql;
using RagBook.Shared.Persistence;

namespace RagBook.Infrastructure.SharedContext.Persistence;

/// <summary>
/// PostgreSQL-aware implementation of <see cref="IPersistenceExceptionClassifier"/>. Maps Npgsql
/// SQLSTATE codes and EF concurrency exceptions to provider-agnostic <see cref="PersistenceErrorKind"/>
/// so module exception handlers can translate them into domain codes.
/// </summary>
public sealed class NpgsqlPersistenceExceptionClassifier : IPersistenceExceptionClassifier
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    /// <inheritdoc />
    public PersistenceErrorKind Classify(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return PersistenceErrorKind.ConcurrencyConflict;
        }

        var postgresException = FindPostgresException(exception);

        return postgresException?.SqlState switch
        {
            UniqueViolation => PersistenceErrorKind.UniqueViolation,
            ForeignKeyViolation => PersistenceErrorKind.ForeignKeyViolation,
            _ => PersistenceErrorKind.Unknown,
        };
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }
}

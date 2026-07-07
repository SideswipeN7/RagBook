using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RagBook.Shared.Auditing;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditable"/> fields centrally using <see cref="TimeProvider"/> (never
/// <c>DateTime.UtcNow</c>) and the current session as the actor (constitution §VI).
/// </summary>
public sealed class AuditingInterceptor(ISessionContext sessionContext, TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var actor = sessionContext.UserSessionId == Guid.Empty
            ? "system"
            : sessionContext.UserSessionId.ToString();

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
                entry.Entity.ModifiedBy = actor;
            }
        }
    }
}

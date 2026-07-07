using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RagBook.Shared.Sessions;

namespace RagBook.Infrastructure.SharedContext.Interceptors;

/// <summary>
/// Stamps <see cref="ISessionOwned.UserSessionId"/> on every newly added session-owned entity from
/// the current <see cref="ISessionContext"/>, so handlers never set the owning session by hand
/// (constitution §III/§VI). Set via the change tracker to honour the entity's private setter.
/// </summary>
public sealed class SessionStampingInterceptor(ISessionContext sessionContext) : SaveChangesInterceptor
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

        foreach (var entry in context.ChangeTracker.Entries<ISessionOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(ISessionOwned.UserSessionId)).CurrentValue = sessionContext.UserSessionId;
            }
        }
    }
}

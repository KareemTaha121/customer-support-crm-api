using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CustomerSupportCrm.Infrastructure.Persistence.Interceptors;

/// <summary>Stamps <see cref="IAuditableEntity"/> creation and modification fields.</summary>
internal sealed class AuditableEntityInterceptor(TimeProvider time, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        Guid? actor = currentUser.IsAuthenticated ? currentUser.UserId.Value : null;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(IAuditableEntity.CreatedAt)).CurrentValue = now;
                entry.Property(nameof(IAuditableEntity.CreatedBy)).CurrentValue = actor;
            }
            else if (entry.State == EntityState.Modified || entry.Collections.Any(collection => collection.IsModified))
            {
                // Child-only changes (e.g. role permissions) still count as modifying the aggregate.
                entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = now;
                entry.Property(nameof(IAuditableEntity.UpdatedBy)).CurrentValue = actor;
            }
        }
    }
}

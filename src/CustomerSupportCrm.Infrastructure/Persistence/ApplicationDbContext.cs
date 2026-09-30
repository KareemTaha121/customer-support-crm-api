using System.Linq.Expressions;
using CustomerSupportCrm.Application.Abstractions.Messaging;
using CustomerSupportCrm.Application.Abstractions.Notifications;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Notifications;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
using CustomerSupportCrm.Infrastructure.Persistence.Configurations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed partial class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IPublisher publisher,
    IRealtimeNotifier realtime)
    : DbContext(options), IApplicationDbContext
{
    private const int MaxDispatchRounds = 10;

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>
    /// Dispatches domain events (handlers join this unit of work), saves, then pushes new
    /// notifications to connected clients.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(cancellationToken);

        var created = ChangeTracker.Entries<Notification>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var notification in created)
        {
            await realtime.SendToUserAsync(
                notification.RecipientId,
                RealtimeEvents.NotificationCreated,
                new { notification.Id, notification.Type, notification.Title, notification.Link, notification.CreatedAt },
                cancellationToken);
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ApplySoftDeleteFilters(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserId>().HaveConversion<StronglyTypedIdConverters.UserIdConverter>();
        configurationBuilder.Properties<RoleId>().HaveConversion<StronglyTypedIdConverters.RoleIdConverter>();
    }

    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        var softDeletable = modelBuilder.Model.GetEntityTypes()
            .Where(type => type.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(type.ClrType))
            .ToList();

        foreach (var entityType in softDeletable)
        {
            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var isDeleted = Expression.Call(
                typeof(EF),
                nameof(EF.Property),
                [typeof(bool)],
                parameter,
                Expression.Constant(nameof(ISoftDeletable.IsDeleted)));

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(Expression.Not(isDeleted), parameter));
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        for (var round = 0; round < MaxDispatchRounds; round++)
        {
            var events = ChangeTracker.Entries<IHasDomainEvents>()
                .SelectMany(entry => entry.Entity.DequeueDomainEvents())
                .ToList();

            if (events.Count == 0)
            {
                return;
            }

            foreach (var domainEvent in events)
            {
                await publisher.Publish(DomainEventNotification.Wrap(domainEvent), cancellationToken);
            }
        }

        throw new InvalidOperationException("Domain event handlers kept raising events; dispatch did not settle.");
    }
}

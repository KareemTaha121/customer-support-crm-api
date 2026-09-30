using CustomerSupportCrm.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

public partial interface IApplicationDbContext
{
    DbSet<ApiKey> ApiKeys { get; }

    DbSet<WebhookSubscription> WebhookSubscriptions { get; }

    DbSet<WebhookDelivery> WebhookDeliveries { get; }

    DbSet<SystemSetting> SystemSettings { get; }
}

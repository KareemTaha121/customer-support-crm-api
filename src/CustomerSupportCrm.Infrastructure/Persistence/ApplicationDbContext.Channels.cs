using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed partial class ApplicationDbContext
{
    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();

    public DbSet<InboundReceipt> InboundReceipts => Set<InboundReceipt>();

    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
}

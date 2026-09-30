using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

public partial interface IApplicationDbContext
{
    DbSet<OutboundMessage> OutboundMessages { get; }

    DbSet<InboundReceipt> InboundReceipts { get; }

    DbSet<ChatConversation> ChatConversations { get; }
}

using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

public partial interface IApplicationDbContext
{
    DbSet<Ticket> Tickets { get; }

    DbSet<TicketMessage> TicketMessages { get; }

    DbSet<TicketHistory> TicketHistory { get; }

    DbSet<TicketCategory> TicketCategories { get; }

    DbSet<CustomerAccount> CustomerAccounts { get; }
}

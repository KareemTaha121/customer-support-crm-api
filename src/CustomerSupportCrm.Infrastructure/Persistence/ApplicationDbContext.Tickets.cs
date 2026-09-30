using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed partial class ApplicationDbContext
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();

    public DbSet<TicketHistory> TicketHistory => Set<TicketHistory>();

    public DbSet<TicketCategory> TicketCategories => Set<TicketCategory>();

    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
}

using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed partial class ApplicationDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();

    public DbSet<CustomerActivity> CustomerActivities => Set<CustomerActivity>();

    public DbSet<Attachment> Attachments => Set<Attachment>();
}

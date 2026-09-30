using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

public partial interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }

    DbSet<CustomerNote> CustomerNotes { get; }

    DbSet<CustomerActivity> CustomerActivities { get; }

    DbSet<Attachment> Attachments { get; }
}

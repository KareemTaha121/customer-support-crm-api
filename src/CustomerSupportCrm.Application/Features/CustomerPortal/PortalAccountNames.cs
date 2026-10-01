using CustomerSupportCrm.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.CustomerPortal;

/// <summary>
/// Keeps portal account names in step when staff or an integration rename a customer. The portal
/// profile and customer tokens read <c>CustomerAccount.DisplayName</c>, not <c>Customer.Name</c>.
/// </summary>
internal static class PortalAccountNames
{
    /// <summary>
    /// Renames the customer's portal accounts that carried the previous customer name. Accounts with a
    /// name of their own (e.g. a contact who registered for a company) keep it. The caller saves.
    /// </summary>
    public static async Task FollowCustomerRenameAsync(IApplicationDbContext db, Guid customerId, string previousName, string newName, CancellationToken cancellationToken)
    {
        if (previousName == newName)
        {
            return;
        }

        var accounts = await db.CustomerAccounts
            .Where(a => a.CustomerId == customerId && a.DisplayName == previousName)
            .ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            account.Rename(newName);
        }
    }
}

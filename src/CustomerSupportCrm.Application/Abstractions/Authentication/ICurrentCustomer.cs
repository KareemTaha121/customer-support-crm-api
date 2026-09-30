namespace CustomerSupportCrm.Application.Abstractions.Authentication;

/// <summary>The signed-in customer of a portal request.</summary>
public interface ICurrentCustomer
{
    bool IsAuthenticated { get; }

    /// <summary>The portal account id. Throws when the caller is not a customer.</summary>
    Guid AccountId { get; }

    /// <summary>The customer record the account belongs to. Throws when the caller is not a customer.</summary>
    Guid CustomerId { get; }
}

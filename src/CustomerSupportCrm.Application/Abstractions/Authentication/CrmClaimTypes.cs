namespace CustomerSupportCrm.Application.Abstractions.Authentication;

/// <summary>JWT claim names. Inbound claim mapping is disabled, so these are used verbatim.</summary>
public static class CrmClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string Permission = "permission";
    public const string SessionId = "sid";

    /// <summary><see cref="ActorTypes"/>: who the token was issued to.</summary>
    public const string Actor = "actor";

    /// <summary>Customer id, on customer-portal tokens only.</summary>
    public const string CustomerId = "cid";
}

public static class ActorTypes
{
    public const string Staff = "staff";
    public const string Customer = "customer";
}

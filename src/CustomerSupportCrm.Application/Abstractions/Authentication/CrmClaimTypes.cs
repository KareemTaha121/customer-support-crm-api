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
}

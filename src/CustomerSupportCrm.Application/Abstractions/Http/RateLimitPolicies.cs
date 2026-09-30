namespace CustomerSupportCrm.Application.Abstractions.Http;

public static class RateLimitPolicies
{
    /// <summary>Strict per-client limit for login, refresh and password endpoints.</summary>
    public const string Authentication = "authentication";
}

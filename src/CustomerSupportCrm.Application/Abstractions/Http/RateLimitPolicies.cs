namespace CustomerSupportCrm.Application.Abstractions.Http;

public static class RateLimitPolicies
{
    /// <summary>Strict per-client limit for login, refresh and password endpoints.</summary>
    public const string Authentication = "authentication";

    /// <summary>Per-client limit for anonymous endpoints (web forms, portal sign-up, feedback, chat).</summary>
    public const string Public = "public";
}

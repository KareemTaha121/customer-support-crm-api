namespace CustomerSupportCrm.Application.Abstractions.Http;

public static class RateLimitPolicies
{
    /// <summary>Strict per-client limit for login, refresh and password endpoints.</summary>
    public const string Authentication = "authentication";

    /// <summary>Per-client limit for anonymous endpoints (web forms, portal sign-up, feedback, chat).</summary>
    public const string Public = "public";

    /// <summary>Knowledge base article votes: a few per client and article per day, so ratings cannot be stuffed.</summary>
    public const string ArticleFeedback = "article-feedback";

    /// <summary>Per-client limit for AI endpoints (cost control).</summary>
    public const string Ai = "ai";
}

namespace CustomerSupportCrm.Infrastructure.Persistence.Seed;

/// <summary>
/// The first administrator, created only when the users table is empty. Supply the password
/// from a secret store or environment variable, and change it after first sign-in.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; init; }

    public string AdminDisplayName { get; init; } = "Administrator";

    public string? AdminPassword { get; init; }

    /// <summary>Name of the organization created on first initialization.</summary>
    public string OrganizationName { get; init; } = "Customer Support";
}

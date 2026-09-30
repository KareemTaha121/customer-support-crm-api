using System.Text.RegularExpressions;
using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Organizations;

/// <summary>
/// The tenant running this deployment: profile, defaults and branding. A deployment has
/// exactly one organization; branches and departments sit beneath it.
/// </summary>
public sealed partial class Organization : Entity<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int ContactMaxLength = 254;
    public const int TimeZoneMaxLength = 64;
    public const string InvalidCode = "INVALID_ORGANIZATION";

    public static readonly string[] SupportedCultures = ["en", "ar"];

    private Organization()
    {
        Name = string.Empty;
        DefaultCulture = "en";
        TimeZone = "UTC";
        PrimaryColor = "#1f6feb";
        AccentColor = "#0e9f6e";
    }

    private Organization(Guid id)
        : base(id)
    {
        Name = string.Empty;
        DefaultCulture = "en";
        TimeZone = "UTC";
        PrimaryColor = "#1f6feb";
        AccentColor = "#0e9f6e";
    }

    public string Name { get; private set; }

    public string? SupportEmail { get; private set; }

    public string? SupportPhone { get; private set; }

    public string DefaultCulture { get; private set; }

    /// <summary>IANA time zone used for business-day reporting.</summary>
    public string TimeZone { get; private set; }

    public string PrimaryColor { get; private set; }

    public string AccentColor { get; private set; }

    public string? LogoStorageKey { get; private set; }

    public string? LogoContentType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Organization Create(string name)
    {
        var organization = new Organization(Guid.CreateVersion7());
        organization.UpdateProfile(name, null, null, "en", "UTC");
        return organization;
    }

    public void UpdateProfile(string name, string? supportEmail, string? supportPhone, string defaultCulture, string timeZone)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The organization name is not valid.");
        }

        if (!SupportedCultures.Contains(defaultCulture))
        {
            throw new DomainException(InvalidCode, "The default language is not supported.");
        }

        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > TimeZoneMaxLength)
        {
            throw new DomainException(InvalidCode, "The time zone is not valid.");
        }

        Name = trimmed;
        SupportEmail = string.IsNullOrWhiteSpace(supportEmail) ? null : supportEmail.Trim();
        SupportPhone = string.IsNullOrWhiteSpace(supportPhone) ? null : supportPhone.Trim();
        DefaultCulture = defaultCulture;
        TimeZone = timeZone.Trim();
    }

    public void UpdateBranding(string primaryColor, string accentColor)
    {
        if (!HexColor().IsMatch(primaryColor) || !HexColor().IsMatch(accentColor))
        {
            throw new DomainException(InvalidCode, "Colors must be #RRGGBB hex values.");
        }

        PrimaryColor = primaryColor.ToUpperInvariant();
        AccentColor = accentColor.ToUpperInvariant();
    }

    public void SetLogo(string storageKey, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        LogoStorageKey = storageKey;
        LogoContentType = contentType;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}

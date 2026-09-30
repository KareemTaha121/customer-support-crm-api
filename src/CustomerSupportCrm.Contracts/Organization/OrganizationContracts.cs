namespace CustomerSupportCrm.Contracts.Organization;

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string? SupportEmail,
    string? SupportPhone,
    string DefaultCulture,
    string TimeZone,
    string PrimaryColor,
    string AccentColor,
    string? LogoUrl);

public sealed record UpdateOrganizationRequest(string Name, string? SupportEmail, string? SupportPhone, string DefaultCulture, string TimeZone);

public sealed record UpdateBrandingRequest(string PrimaryColor, string AccentColor);

/// <summary>Anonymous branding for the login page, customer portal and emails.</summary>
public sealed record PublicBrandingResponse(string Name, string DefaultCulture, string PrimaryColor, string AccentColor, string? LogoUrl);

public sealed record BranchRequest(string Code, string Name, string? Address, string? Phone);

public sealed record DepartmentRequest(string Code, string Name, string? Email);

public sealed record DepartmentResponse(Guid Id, Guid BranchId, string Code, string Name, string? Email, bool IsActive);

public sealed record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    string? Phone,
    bool IsActive,
    IReadOnlyList<DepartmentResponse> Departments);

using System.Globalization;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Application.Abstractions.Authentication;

/// <summary>
/// The caller of the current request. Application code uses this instead of HttpContext.User.
/// Organization, branch and department context is added in Phase 3.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The caller's id. Throws when the request is anonymous.</summary>
    UserId UserId { get; }

    /// <summary>The refresh-token session the access token was issued for, if any.</summary>
    Guid? SessionId { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    CultureInfo Culture { get; }

    bool HasPermission(string permission);
}

namespace CustomerSupportCrm.Contracts.Common;

/// <param name="Code">Stable, language-neutral machine-readable code (see <see cref="ErrorCodes"/>).</param>
/// <param name="Message">Localized, user-facing message.</param>
/// <param name="Field">camelCase request field the error applies to, when applicable.</param>
public sealed record ApiError(string Code, string Message, string? Field = null);

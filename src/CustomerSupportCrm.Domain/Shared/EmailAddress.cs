using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Shared;

/// <summary>
/// A trimmed, lower-cased email address. Only structural checks are made here;
/// deliverability is not a domain concern.
/// </summary>
public sealed record EmailAddress
{
    public const int MaxLength = 254;

    public const string InvalidCode = "INVALID_EMAIL_ADDRESS";

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress Create(string? value)
    {
        var normalized = Normalize(value);

        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxLength || !IsWellFormed(normalized))
        {
            throw new DomainException(InvalidCode, "The email address is not valid.");
        }

        return new EmailAddress(normalized);
    }

    /// <summary>The canonical form used for storage and lookups, without validation.</summary>
    public static string Normalize(string? value) =>
#pragma warning disable CA1308 // Email addresses are conventionally compared in lower case.
        value?.Trim().ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308

    public override string ToString() => Value;

    private static bool IsWellFormed(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0
            && at == value.LastIndexOf('@')
            && at < value.Length - 1
            && value.AsSpan(at + 1).Contains('.')
            && !value.Any(char.IsWhiteSpace);
    }
}

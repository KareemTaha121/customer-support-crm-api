using System.Text.RegularExpressions;
using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Shared;

/// <summary>
/// A phone number in E.164 form (+9665XXXXXXXX). Spaces, dashes, dots and parentheses are
/// removed and a leading "00" becomes "+"; national formats are rejected.
/// </summary>
public sealed partial record PhoneNumber
{
    public const int MaxLength = 16;
    public const string InvalidCode = "INVALID_PHONE_NUMBER";

    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public static PhoneNumber Create(string? value)
    {
        var normalized = Normalize(value);
        if (!E164().IsMatch(normalized))
        {
            throw new DomainException(InvalidCode, "The phone number must be in international format, e.g. +966501234567.");
        }

        return new PhoneNumber(normalized);
    }

    public static bool TryCreate(string? value, out PhoneNumber? phone)
    {
        var normalized = Normalize(value);
        phone = E164().IsMatch(normalized) ? new PhoneNumber(normalized) : null;
        return phone is not null;
    }

    public static string Normalize(string? value)
    {
        var digits = new string([.. (value ?? string.Empty).Where(c => char.IsAsciiDigit(c) || c == '+')]);
        return digits.StartsWith("00", StringComparison.Ordinal) ? "+" + digits[2..] : digits;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();
}

namespace CustomerSupportCrm.Domain.Common;

/// <summary>
/// Raised when a business invariant or state transition rule is violated.
/// </summary>
/// <param name="code">Stable error code, e.g. TICKET_ALREADY_CLOSED.</param>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

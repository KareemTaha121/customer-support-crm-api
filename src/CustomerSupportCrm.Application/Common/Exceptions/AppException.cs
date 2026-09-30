namespace CustomerSupportCrm.Application.Common.Exceptions;

/// <summary>
/// Base for expected application failures that map to a stable error code.
/// The message is a fallback; the API localizes by <see cref="Code"/>.
/// </summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

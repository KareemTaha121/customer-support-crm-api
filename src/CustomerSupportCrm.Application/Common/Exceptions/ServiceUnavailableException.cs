using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Application.Common.Exceptions;

/// <summary>A dependency (e.g. the AI provider) is not configured or temporarily unavailable. Maps to 503.</summary>
public sealed class ServiceUnavailableException(string code = ErrorCodes.ServiceUnavailable, string message = "The service is temporarily unavailable.")
    : AppException(code, message);

/// <summary>The request was understood but cannot be processed (e.g. declined by a policy). Maps to 422.</summary>
public sealed class UnprocessableException(string code = ErrorCodes.BusinessRuleViolation, string message = "The request cannot be processed.")
    : AppException(code, message);

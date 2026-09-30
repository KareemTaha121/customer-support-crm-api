using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Application.Common.Exceptions;

public sealed class UnauthorizedException(string code = ErrorCodes.Unauthorized, string message = "Authentication is required.")
    : AppException(code, message);

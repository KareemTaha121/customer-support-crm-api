using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Application.Common.Exceptions;

public sealed class ForbiddenException(string code = ErrorCodes.Forbidden, string message = "You do not have permission to perform this action.")
    : AppException(code, message);

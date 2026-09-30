using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Application.Common.Exceptions;

public sealed class NotFoundException(string code = ErrorCodes.NotFound, string message = "The requested resource was not found.")
    : AppException(code, message);

using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Application.Common.Exceptions;

public sealed class ConflictException(string code = ErrorCodes.Conflict, string message = "The request conflicts with the current state of the resource.")
    : AppException(code, message);

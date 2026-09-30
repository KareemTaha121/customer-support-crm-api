namespace CustomerSupportCrm.Contracts.Common;

/// <summary>
/// The envelope every API response uses. See docs/api-contract.md.
/// </summary>
public sealed record ApiResponse<T>
{
    public required bool Success { get; init; }

    public T? Data { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<ApiError> Errors { get; init; } = [];

    public PaginationMeta? Meta { get; init; }

    public string? CorrelationId { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null, PaginationMeta? meta = null) =>
        new() { Success = true, Data = data, Message = message, Meta = meta };

    public static ApiResponse<object> Failure(string message, IReadOnlyList<ApiError> errors) =>
        new() { Success = false, Message = message, Errors = errors };
}

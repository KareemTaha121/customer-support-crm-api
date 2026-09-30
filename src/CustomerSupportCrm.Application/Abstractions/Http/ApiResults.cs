using CustomerSupportCrm.Contracts.Common;
using Microsoft.AspNetCore.Http;

namespace CustomerSupportCrm.Application.Abstractions.Http;

/// <summary>
/// Success results wrapped in the standard <see cref="ApiResponse{T}"/> envelope.
/// Error responses are produced centrally by the API's exception handler.
/// </summary>
public static class ApiResults
{
    public static ApiResult<T> Ok<T>(T data, string? message = null) =>
        new(StatusCodes.Status200OK, ApiResponse.Ok(data, message));

    public static ApiResult<T> Created<T>(string location, T data, string? message = null) =>
        new(StatusCodes.Status201Created, ApiResponse.Ok(data, message), location);

    public static ApiResult<IReadOnlyList<T>> Paged<T>(IReadOnlyList<T> items, PaginationMeta meta) =>
        new(StatusCodes.Status200OK, ApiResponse.Ok(items, meta: meta));
}

public sealed class ApiResult<T>(int statusCode, ApiResponse<T> response, string? location = null)
    : IResult, IStatusCodeHttpResult, IValueHttpResult<ApiResponse<T>>
{
    public int StatusCode => statusCode;

    int? IStatusCodeHttpResult.StatusCode => statusCode;

    public ApiResponse<T> Value => response;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = statusCode;
        if (location is not null)
        {
            httpContext.Response.Headers.Location = location;
        }

        var body = response with { CorrelationId = httpContext.GetCorrelationId() };
        return httpContext.Response.WriteAsJsonAsync(body, httpContext.RequestAborted);
    }
}

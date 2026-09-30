using Microsoft.AspNetCore.Http;

namespace CustomerSupportCrm.Application.Abstractions.Http;

public static class CorrelationIdHttpContextExtensions
{
    public const string HeaderName = "X-Correlation-Id";

    private static readonly object ItemKey = new();

    public static string? GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;

    public static void SetCorrelationId(this HttpContext context, string correlationId) =>
        context.Items[ItemKey] = correlationId;
}

namespace CustomerSupportCrm.Application.Abstractions.Http;

/// <summary>Transport details of the current request, for auditing and session tracking.</summary>
public interface IRequestContext
{
    string? CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }
}

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

/// <summary>Gap-tolerant, concurrency-safe numbers from database sequences.</summary>
public interface ISequenceGenerator
{
    Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken);
}

public static class Sequences
{
    public const string CustomerNumbers = "customer_numbers";
    public const string TicketNumbers = "ticket_numbers";
}

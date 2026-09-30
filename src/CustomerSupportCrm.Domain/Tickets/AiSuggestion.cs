using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tickets;

/// <summary>
/// Log of one AI result (summary, reply draft, categorization, solutions, chatbot answer):
/// what was produced, by which model, at what cost, and whether the agent used it.
/// </summary>
public sealed class AiSuggestion : Entity<Guid>
{
    private AiSuggestion()
    {
        Feature = string.Empty;
        Model = string.Empty;
        Output = string.Empty;
    }

    private AiSuggestion(Guid id)
        : base(id)
    {
        Feature = string.Empty;
        Model = string.Empty;
        Output = string.Empty;
    }

    public string Feature { get; private set; }

    public Guid? TicketId { get; private set; }

    public UserId? RequestedBy { get; private set; }

    public string Model { get; private set; }

    /// <summary>JSON output as returned to the client.</summary>
    public string Output { get; private set; }

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    /// <summary>Agent feedback: used/accepted (true), discarded (false), unknown (null).</summary>
    public bool? Accepted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AiSuggestion Record(string feature, Guid? ticketId, UserId? requestedBy, string model, string output, long inputTokens, long outputTokens, DateTimeOffset now) =>
        new(Guid.CreateVersion7())
        {
            Feature = feature,
            TicketId = ticketId,
            RequestedBy = requestedBy,
            Model = model,
            Output = output,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CreatedAt = now,
        };

    public void RecordFeedback(bool accepted) => Accepted = accepted;
}

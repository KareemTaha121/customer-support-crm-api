using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Tickets;

namespace CustomerSupportCrm.Domain.Sla;

/// <summary>
/// Response and resolution targets per priority. A policy may be limited to a category and/or
/// department; the most specific active policy wins, then the default policy. Targets can run on
/// business hours (organization time zone) or around the clock.
/// </summary>
public sealed class SlaPolicy : AggregateRoot<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 150;
    public const string InvalidCode = "INVALID_SLA_POLICY";

    private readonly List<SlaTargetTime> _targets = [];

    private SlaPolicy()
    {
        Name = string.Empty;
        WorkDays = [];
    }

    private SlaPolicy(Guid id)
        : base(id)
    {
        Name = string.Empty;
        WorkDays = [];
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsDefault { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public bool BusinessHoursOnly { get; private set; }

    /// <summary>Working days (0 = Sunday … 6 = Saturday) when <see cref="BusinessHoursOnly"/>.</summary>
    public List<int> WorkDays { get; private set; }

    public TimeOnly WorkStart { get; private set; }

    public TimeOnly WorkEnd { get; private set; }

    public IReadOnlyCollection<SlaTargetTime> Targets => _targets;

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static SlaPolicy Create(string name) => new(Guid.CreateVersion7()) { Name = name.Trim(), IsActive = true };

    public void Update(
        string name,
        string? description,
        bool isActive,
        bool isDefault,
        Guid? categoryId,
        Guid? departmentId,
        bool businessHoursOnly,
        IEnumerable<int> workDays,
        TimeOnly workStart,
        TimeOnly workEnd,
        IEnumerable<(TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes)> targets)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The policy name is not valid.");
        }

        var days = workDays.Distinct().Order().ToList();
        if (businessHoursOnly && (days.Count == 0 || days.Any(d => d is < 0 or > 6) || workEnd <= workStart))
        {
            throw new DomainException(InvalidCode, "Business hours need at least one working day and an end after the start.");
        }

        var targetList = targets.ToList();
        if (targetList.Count == 0 || targetList.Select(t => t.Priority).Distinct().Count() != targetList.Count
            || targetList.Any(t => t.FirstResponseMinutes <= 0 || t.ResolutionMinutes <= 0 || t.ResolutionMinutes < t.FirstResponseMinutes))
        {
            throw new DomainException(InvalidCode, "Each priority needs one target, with resolution ≥ first response > 0.");
        }

        Name = trimmed;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        IsDefault = isDefault;
        CategoryId = categoryId;
        DepartmentId = departmentId;
        BusinessHoursOnly = businessHoursOnly;
        WorkDays = days;
        WorkStart = workStart;
        WorkEnd = workEnd;

        _targets.Clear();
        _targets.AddRange(targetList.Select(t => new SlaTargetTime(Id, t.Priority, t.FirstResponseMinutes, t.ResolutionMinutes)));
    }

    public void ClearDefault() => IsDefault = false;

    public SlaTargetTime? TargetFor(TicketPriority priority) => _targets.FirstOrDefault(t => t.Priority == priority);

    /// <summary>Adds working minutes to <paramref name="start"/> in the given time zone.</summary>
    public DateTimeOffset AddWorkingMinutes(DateTimeOffset start, int minutes, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (!BusinessHoursOnly)
        {
            return start.AddMinutes(minutes);
        }

        var local = TimeZoneInfo.ConvertTime(start, timeZone);
        var remaining = (double)minutes;

        // Bounded walk: at most ~2 years of days, far beyond any SLA target.
        for (var guard = 0; guard < 800 && remaining > 0; guard++)
        {
            var day = local.Date;
            var dayStart = new DateTimeOffset(day.Add(WorkStart.ToTimeSpan()), timeZone.GetUtcOffset(day.Add(WorkStart.ToTimeSpan())));
            var dayEnd = new DateTimeOffset(day.Add(WorkEnd.ToTimeSpan()), timeZone.GetUtcOffset(day.Add(WorkEnd.ToTimeSpan())));

            if (WorkDays.Contains((int)day.DayOfWeek) && local < dayEnd)
            {
                var from = local > dayStart ? local : dayStart;
                var available = (dayEnd - from).TotalMinutes;
                if (available >= remaining)
                {
                    return from.AddMinutes(remaining).ToUniversalTime();
                }

                remaining -= available;
            }

            var next = day.AddDays(1).Add(WorkStart.ToTimeSpan());
            local = new DateTimeOffset(next, timeZone.GetUtcOffset(next));
        }

        return local.ToUniversalTime();
    }
}

public sealed class SlaTargetTime
{
    private SlaTargetTime()
    {
    }

    internal SlaTargetTime(Guid policyId, TicketPriority priority, int firstResponseMinutes, int resolutionMinutes)
    {
        PolicyId = policyId;
        Priority = priority;
        FirstResponseMinutes = firstResponseMinutes;
        ResolutionMinutes = resolutionMinutes;
    }

    public Guid PolicyId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public int FirstResponseMinutes { get; private set; }

    public int ResolutionMinutes { get; private set; }
}

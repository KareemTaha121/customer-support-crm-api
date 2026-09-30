using System.ComponentModel.DataAnnotations;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>Logs parameter values. Development only; never enable in production.</summary>
    public bool EnableSensitiveDataLogging { get; init; }
}

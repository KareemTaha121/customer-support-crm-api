using System.ComponentModel.DataAnnotations;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Apply migrations and seed data at startup. Development only; other environments run
    /// the API once with --init-database as a deployment step.
    /// </summary>
    public bool InitializeOnStartup { get; init; }

    /// <summary>Logs parameter values. Development only; never enable in production.</summary>
    public bool EnableSensitiveDataLogging { get; init; }
}

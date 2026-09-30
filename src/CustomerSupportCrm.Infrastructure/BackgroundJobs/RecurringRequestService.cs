using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Infrastructure.BackgroundJobs;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>Disable in test hosts and in API replicas that should not run jobs.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Sends <typeparamref name="TRequest"/> through MediatR on a fixed interval, in a fresh scope.
/// Jobs must be idempotent: work is persisted state (outbox rows, SLA timestamps), so a restart
/// resumes where it stopped.
/// </summary>
internal sealed partial class RecurringRequestService<TRequest>(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobOptions> options,
    ILogger<RecurringRequestService<TRequest>> logger,
    TimeSpan interval)
    : BackgroundService
    where TRequest : IRequest, new()
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TRequest(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogJobFailed(logger, typeof(TRequest).Name, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background job {Job} failed")]
    private static partial void LogJobFailed(ILogger logger, string job, Exception exception);
}

public static class RecurringJobRegistration
{
    public static IServiceCollection AddRecurringRequest<TRequest>(this IServiceCollection services, TimeSpan interval)
        where TRequest : IRequest, new() =>
        services.AddHostedService(provider => ActivatorUtilities.CreateInstance<RecurringRequestService<TRequest>>(provider, interval));
}

using Rotadesk.Infrastructure.Outbox;

namespace Rotadesk.Api.Outbox;

/// <summary>
/// Sends due emails every few seconds, in a fresh scope each time. A failed run is logged and the next one tries
/// again; messages are never lost, because they stay in the table until they're marked sent or given up on.
/// </summary>
internal sealed partial class OutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OutboxWorker> log) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Outbox:PollSeconds", 5.0));
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(BatchSize, stoppingToken);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    LogRunFailed(log, error);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. A message being sent right now keeps its lease and goes out on the next start.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox run failed; the next one will try again.")]
    private static partial void LogRunFailed(ILogger logger, Exception error);
}

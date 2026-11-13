namespace Workbench.Host.Observation;

using Microsoft.Extensions.Hosting;
using Workbench.Host.Sources.Claude;

internal sealed class ObservationLoop(
    ClaudeObservationSource source,
    ObservationStore store,
    ObservationSettings settings,
    Func<TimeSpan, CancellationToken, Task> wait) : BackgroundService
{
    internal ObservationLoop(
        ClaudeObservationSource source,
        ObservationStore store,
        ObservationSettings settings,
        TimeProvider clock)
        : this(source, store, settings, (interval, cancellationToken) => Task.Delay(interval, clock, cancellationToken))
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var attempt = await source.CaptureAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            store.Publish(attempt);
            try
            {
                await wait(settings.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}

namespace Workbench.Host.Observation;

internal sealed record ObservationSettings(TimeSpan PollInterval, TimeSpan StaleAfter)
{
    internal const int DefaultPollIntervalSeconds = 15;

    internal const int DefaultStaleAfterSeconds = 45;

    internal static ObservationSettings Default { get; } = new(
        TimeSpan.FromSeconds(DefaultPollIntervalSeconds),
        TimeSpan.FromSeconds(DefaultStaleAfterSeconds));
}

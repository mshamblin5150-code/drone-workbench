namespace Workbench.Host.Observation;

using Workbench.Host.Sources.Claude;

internal sealed record ObservationState(
    long Revision,
    ClaudeObservation? LatestAttempt,
    ClaudeObservation? LatestObservation,
    int ConsecutiveFailures)
{
    internal static ObservationState Empty { get; } = new(0, null, null, 0);

    internal static bool Succeeded(ClaudeObservation attempt) =>
        attempt.State is ClaudeObservation.CaptureState.Observed or ClaudeObservation.CaptureState.Partial;

    internal ObservationState With(ClaudeObservation attempt)
    {
        var succeeded = Succeeded(attempt);
        return new(
            this.Revision + 1,
            attempt,
            succeeded ? attempt : this.LatestObservation,
            succeeded ? 0 : this.ConsecutiveFailures + 1);
    }
}

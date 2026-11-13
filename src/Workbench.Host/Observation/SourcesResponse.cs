namespace Workbench.Host.Observation;

using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core;
using Workbench.Host.Sources.Claude;

internal sealed record SourcesResponse(
    int Schema,
    long Revision,
    SourcesResponse.SettingsView Settings,
    IReadOnlyList<SourcesResponse.SourceView> Sources)
{
    public enum Freshness
    {
        Pending,
        Fresh,
        Stale,
        None,
    }

    public enum StaleReason
    {
        LatestAttemptFailed,
        ObservationExpired,
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static (Freshness Freshness, StaleReason? Reason) Assess(
        ObservationState state,
        ObservationSettings settings,
        DateTimeOffset now)
    {
        if (state.LatestAttempt is not { } attempt)
        {
            return (Freshness.Pending, null);
        }

        if (state.LatestObservation is not { } observation)
        {
            return (Freshness.None, null);
        }

        if (!ObservationState.Succeeded(attempt))
        {
            return (Freshness.Stale, StaleReason.LatestAttemptFailed);
        }

        return now - ObservedAt(observation) > settings.StaleAfter
            ? (Freshness.Stale, StaleReason.ObservationExpired)
            : (Freshness.Fresh, null);
    }

    internal static SourcesResponse Project(ObservationState state, ObservationSettings settings, DateTimeOffset now)
    {
        var (freshness, reason) = Assess(state, settings, now);
        var attempt = state.LatestAttempt;
        var observation = state.LatestObservation;
        var gaps = (observation?.CoverageGaps ?? [])
            .Concat(attempt?.CoverageGaps ?? ClaudeObservation.DefaultGaps)
            .Distinct()
            .ToList();
        long? age = observation is null
            ? null
            : (long)Math.Max(0, Math.Floor((now - ObservedAt(observation)).TotalSeconds));
        var source = new SourceView(
            "claude",
            freshness,
            reason,
            attempt is null ? null : new AttemptView(attempt.AttemptedAt, attempt.State, attempt.Failure),
            state.ConsecutiveFailures,
            observation,
            age,
            "Unknown",
            false,
            gaps);
        return new(
            LocalChannel.SchemaVersion,
            state.Revision,
            new(settings.PollInterval.TotalSeconds, settings.StaleAfter.TotalSeconds),
            [source]);
    }

    internal static string Serialize(ObservationState state, ObservationSettings settings, DateTimeOffset now) =>
        JsonSerializer.Serialize(Project(state, settings, now), JsonOptions);

    private static DateTimeOffset ObservedAt(ClaudeObservation observation) =>
        observation.ObservedAt ?? observation.AttemptedAt;

    public sealed record SettingsView(double PollIntervalSeconds, double StaleAfterSeconds);

    public sealed record AttemptView(
        DateTimeOffset AttemptedAt,
        ClaudeObservation.CaptureState State,
        ClaudeObservation.FailureReason Failure);

    public sealed record SourceView(
        string Source,
        Freshness Freshness,
        StaleReason? StaleReason,
        AttemptView? LastAttempt,
        int ConsecutiveFailures,
        ClaudeObservation? Observation,
        long? ObservationAgeSeconds,
        string SupervisorHealth,
        bool ControlsAvailable,
        IReadOnlyList<ClaudeObservation.CoverageGap> CoverageGaps);
}

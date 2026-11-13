namespace Workbench.Host.Tests.Observation;

using System.Text.Json;
using Workbench.Host.Observation;
using Workbench.Host.Sources.Claude;
using Xunit;
using static Workbench.Host.Tests.Observation.SyntheticClaude;

public sealed class SourcesProjectionTests
{
    private static readonly ObservationSettings Settings = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));

    [Fact]
    public void NoAttemptIsPendingWithoutInventingAnObservation()
    {
        var claude = Project(new ObservationStore(), Start);
        Assert.Equal(SourcesResponse.Freshness.Pending, claude.Freshness);
        Assert.Null(claude.StaleReason);
        Assert.Null(claude.LastAttempt);
        Assert.Null(claude.Observation);
        Assert.Null(claude.ObservationAgeSeconds);
        Assert.Equal(ClaudeObservation.DefaultGaps, claude.CoverageGaps);
    }

    [Fact]
    public void FirstSuccessIsFresh()
    {
        var store = new ObservationStore();
        var success = PartialSuccess(Start);
        store.Publish(success);
        var claude = Project(store, Start.AddSeconds(10));
        Assert.Equal(SourcesResponse.Freshness.Fresh, claude.Freshness);
        Assert.Null(claude.StaleReason);
        Assert.Same(success, claude.Observation);
        Assert.Equal(10, claude.ObservationAgeSeconds);
        Assert.Equal(0, claude.ConsecutiveFailures);
        Assert.Equal(1, store.Current.Revision);
    }

    [Fact]
    public void FailureAfterSuccessKeepsTheOldObservationStaleWithItsUncertainty()
    {
        var store = new ObservationStore();
        var success = PartialSuccess(Start);
        store.Publish(success);
        store.Publish(Failure(Start.AddSeconds(15)));
        var claude = Project(store, Start.AddSeconds(16));
        Assert.Equal(SourcesResponse.Freshness.Stale, claude.Freshness);
        Assert.Equal(SourcesResponse.StaleReason.LatestAttemptFailed, claude.StaleReason);
        Assert.Same(success, claude.Observation);
        Assert.Null(claude.Observation!.ProcessCoverage.UnmatchedCount);
        Assert.Equal(ClaudeObservation.WorkState.Unknown, Assert.Single(claude.Observation.Sessions!).State);
        Assert.Equal("Unknown", claude.SupervisorHealth);
        Assert.False(claude.ControlsAvailable);
        Assert.Contains(ClaudeObservation.CoverageGap.ProcessInventoryIncomplete, claude.CoverageGaps);
        Assert.Contains(ClaudeObservation.CoverageGap.UnknownSessionState, claude.CoverageGaps);
        Assert.Contains(ClaudeObservation.CoverageGap.SupervisorSignalUnavailable, claude.CoverageGaps);
        Assert.Equal(ClaudeObservation.CaptureState.Disconnected, claude.LastAttempt!.State);
        Assert.Equal(ClaudeObservation.FailureReason.Timeout, claude.LastAttempt.Failure);
        Assert.Equal(1, claude.ConsecutiveFailures);
        Assert.Equal(16, claude.ObservationAgeSeconds);
    }

    [Fact]
    public void FailuresWithoutAnySuccessHaveNoObservation()
    {
        var store = new ObservationStore();
        store.Publish(Failure(Start, ClaudeObservation.CaptureState.Unavailable, ClaudeObservation.FailureReason.MissingExecutable));
        store.Publish(Failure(Start.AddSeconds(15), ClaudeObservation.CaptureState.Unavailable, ClaudeObservation.FailureReason.MissingExecutable));
        var claude = Project(store, Start.AddSeconds(16));
        Assert.Equal(SourcesResponse.Freshness.None, claude.Freshness);
        Assert.Null(claude.StaleReason);
        Assert.Null(claude.Observation);
        Assert.Null(claude.ObservationAgeSeconds);
        Assert.Equal(2, claude.ConsecutiveFailures);
        Assert.Equal(ClaudeObservation.FailureReason.MissingExecutable, claude.LastAttempt!.Failure);
    }

    [Fact]
    public void SuccessOlderThanStaleAfterExpires()
    {
        var store = new ObservationStore();
        store.Publish(PartialSuccess(Start));
        Assert.Equal(SourcesResponse.Freshness.Fresh, Project(store, Start.AddSeconds(45)).Freshness);
        var claude = Project(store, Start.AddSeconds(46));
        Assert.Equal(SourcesResponse.Freshness.Stale, claude.Freshness);
        Assert.Equal(SourcesResponse.StaleReason.ObservationExpired, claude.StaleReason);
        Assert.NotNull(claude.Observation);
    }

    [Fact]
    public void RecoveryResetsConsecutiveFailures()
    {
        var store = new ObservationStore();
        store.Publish(PartialSuccess(Start));
        store.Publish(Failure(Start.AddSeconds(15)));
        store.Publish(Failure(Start.AddSeconds(30)));
        Assert.Equal(2, store.Current.ConsecutiveFailures);
        var recovered = PartialSuccess(Start.AddSeconds(45));
        store.Publish(recovered);
        var claude = Project(store, Start.AddSeconds(46));
        Assert.Equal(SourcesResponse.Freshness.Fresh, claude.Freshness);
        Assert.Same(recovered, claude.Observation);
        Assert.Equal(0, claude.ConsecutiveFailures);
        Assert.Equal(4, store.Current.Revision);
    }

    [Fact]
    public void UnsupportedVersionAfterSuccessKeepsTheOldObservationStale()
    {
        var store = new ObservationStore();
        var success = PartialSuccess(Start);
        store.Publish(success);
        store.Publish(Failure(Start.AddSeconds(15), ClaudeObservation.CaptureState.UnsupportedVersion, ClaudeObservation.FailureReason.UnsupportedVersion));
        var claude = Project(store, Start.AddSeconds(16));
        Assert.Equal(SourcesResponse.Freshness.Stale, claude.Freshness);
        Assert.Equal(SourcesResponse.StaleReason.LatestAttemptFailed, claude.StaleReason);
        Assert.Same(success, claude.Observation);
        Assert.Equal(ClaudeObservation.CaptureState.UnsupportedVersion, claude.LastAttempt!.State);
    }

    [Fact]
    public void SerializedResponseUsesSchemaOneCamelCaseAndStringEnums()
    {
        var store = new ObservationStore();
        store.Publish(PartialSuccess(Start));
        store.Publish(Failure(Start.AddSeconds(15)));
        using var json = JsonDocument.Parse(SourcesResponse.Serialize(store.Current, Settings, Start.AddSeconds(16)));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal(2, root.GetProperty("revision").GetInt64());
        Assert.Equal(15, root.GetProperty("settings").GetProperty("pollIntervalSeconds").GetDouble());
        Assert.Equal(45, root.GetProperty("settings").GetProperty("staleAfterSeconds").GetDouble());
        var claude = Assert.Single(root.GetProperty("sources").EnumerateArray().ToList());
        Assert.Equal("claude", claude.GetProperty("source").GetString());
        Assert.Equal("Stale", claude.GetProperty("freshness").GetString());
        Assert.Equal("LatestAttemptFailed", claude.GetProperty("staleReason").GetString());
        Assert.Equal("Disconnected", claude.GetProperty("lastAttempt").GetProperty("state").GetString());
        Assert.Equal("Timeout", claude.GetProperty("lastAttempt").GetProperty("failure").GetString());
        Assert.Equal(JsonValueKind.Null, claude.GetProperty("observation").GetProperty("processCoverage").GetProperty("unmatchedCount").ValueKind);
        Assert.Equal("Unknown", claude.GetProperty("supervisorHealth").GetString());
        Assert.False(claude.GetProperty("controlsAvailable").GetBoolean());
        Assert.Contains("ProcessInventoryIncomplete", claude.GetProperty("coverageGaps").EnumerateArray().Select(gap => gap.GetString()));
    }

    [Fact]
    public async Task SourceTextAndIdentifiersNeverReachTheResponse()
    {
        const string agents = """
            [{"sessionId":"26d93c03-641e-4199-a713-f5a32823a171","kind":"interactive","status":"idle",
            "pid":4242,"name":"CANARY-NAME","cwd":"C:\\CANARY-CWD","extra":{"note":"CANARY-UNKNOWN"}}]
            """;
        var store = new ObservationStore();
        store.Publish(await Source(new Reader(agents)).CaptureAsync(TestContext.Current.CancellationToken));
        var json = SourcesResponse.Serialize(store.Current, Settings, Start);
        Assert.NotNull(store.Current.LatestObservation);
        Assert.DoesNotContain("CANARY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("26d93c03", json, StringComparison.Ordinal);
        Assert.DoesNotContain("4242", json, StringComparison.Ordinal);
    }

    private static SourcesResponse.SourceView Project(ObservationStore store, DateTimeOffset now) =>
        Assert.Single(SourcesResponse.Project(store.Current, Settings, now).Sources);
}

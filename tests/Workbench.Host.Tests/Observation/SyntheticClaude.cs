namespace Workbench.Host.Tests.Observation;

using System.Text;
using Workbench.Host.Sources.Claude;

internal static class SyntheticClaude
{
    internal static readonly DateTimeOffset Start = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    internal static ClaudeObservation PartialSuccess(DateTimeOffset at) => new(
        ClaudeObservation.CaptureState.Partial,
        at,
        at,
        "2.1.322",
        ClaudeObservation.RootScope.DefaultRoot,
        [new(ClaudeObservation.Entrypoint.Unknown, ClaudeObservation.WorkState.Unknown, false)],
        new(false, 1, null),
        ClaudeObservation.FailureReason.None,
        [.. ClaudeObservation.DefaultGaps, ClaudeObservation.CoverageGap.ProcessInventoryIncomplete, ClaudeObservation.CoverageGap.UnknownSessionState]);

    internal static ClaudeObservation Failure(
        DateTimeOffset at,
        ClaudeObservation.CaptureState state = ClaudeObservation.CaptureState.Disconnected,
        ClaudeObservation.FailureReason failure = ClaudeObservation.FailureReason.Timeout) =>
        new(state, at, null, null, ClaudeObservation.RootScope.DefaultRoot, null, new(false, 0, null), failure, ClaudeObservation.DefaultGaps);

    internal static ClaudeObservationSource Source(IClaudeCommandReader reader) =>
        new(reader, new EmptyInventory(), new FixedClock(Start));

    internal sealed class Reader(string agents) : IClaudeCommandReader
    {
        public Task<ClaudeCommandResult> ReadAsync(ClaudeReadOperation operation, CancellationToken cancellationToken) =>
            Task.FromResult(Result(operation, agents));

        internal static ClaudeCommandResult Result(ClaudeReadOperation operation, string agents) => new(
            ClaudeObservation.FailureReason.None,
            ClaudeObservation.RootScope.DefaultRoot,
            Encoding.UTF8.GetBytes(operation == ClaudeReadOperation.Version ? "2.1.322 (Claude Code)" : agents));
    }

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EmptyInventory : IClaudeProcessReader
    {
        public ClaudeProcessInventory Capture() => new(true, new Dictionary<int, ClaudeProcessInventory.ProcessIdentity>());
    }
}

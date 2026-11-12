namespace Workbench.Host.Tests.Sources.Claude;

using System.Text;
using System.Text.Json;
using Workbench.Host.Sources.Claude;
using Xunit;

public sealed class ClaudeSourceTests
{
    private const string Agent = """
        [{"sessionId":"26d93c03-641e-4199-a713-f5a32823a171",
        "kind":"interactive","status":"idle","pid":123,"name":"CANARY","cwd":"CANARY"}]
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClassifiesDesktopAndCountsUnmatchedProcessesWithoutInventingSessions()
    {
        var processes = Inventory(
            new(123, Now.AddMinutes(-1), 50, ClaudeProcessInventory.ProcessRole.Claude, true),
            new(50, Now.AddMinutes(-2), 0, ClaudeProcessInventory.ProcessRole.DesktopApp, true),
            new(124, Now.AddMinutes(-1), 0, ClaudeProcessInventory.ProcessRole.Claude, true));
        var result = await Source(new Reader(Agent), processes).CaptureAsync(TestContext.Current.CancellationToken);
        var session = Assert.Single(result.Sessions!);
        Assert.Equal(ClaudeObservation.Entrypoint.Desktop, session.Entrypoint);
        Assert.Equal(ClaudeObservation.WorkState.Idle, session.State);
        Assert.True(session.ProcessMatched);
        Assert.Equal(2, result.ProcessCoverage.KnownUnmatchedCount);
        Assert.Equal(2, result.ProcessCoverage.UnmatchedCount);
        Assert.Equal("Unknown", result.SupervisorHealth);
        Assert.False(result.ControlsAvailable);
        Assert.Contains(ClaudeObservation.CoverageGap.CodexOwnerUnreachable, result.CoverageGaps);
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulEmptySnapshotDoesNotInventAnUnlistedIdleSession()
    {
        var result = await Source(
            new Reader("[]"),
            Inventory(new ClaudeProcessInventory.ProcessIdentity(123, Now.AddMinutes(-1), 0, ClaudeProcessInventory.ProcessRole.Claude, true)))
            .CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.CaptureState.Observed, result.State);
        Assert.Empty(result.Sessions!);
        Assert.Equal(1, result.ProcessCoverage.UnmatchedCount);
        Assert.NotNull(result.ObservedAt);
    }

    [Fact]
    public async Task FailedInventoryHasUnknownTotalAndNeverKeepsIdleFromSourceAlone()
    {
        var result = await Source(new Reader(Agent), new(false, new Dictionary<int, ClaudeProcessInventory.ProcessIdentity>()))
            .CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.CaptureState.Partial, result.State);
        Assert.Null(result.ProcessCoverage.UnmatchedCount);
        Assert.False(result.ProcessCoverage.Complete);
        Assert.Equal(ClaudeObservation.WorkState.Unknown, Assert.Single(result.Sessions!).State);
    }

    [Fact]
    public async Task SourceFailureIsNotAHealthyEmptySnapshot()
    {
        foreach (var (failure, expectedState) in new[]
        {
            (ClaudeObservation.FailureReason.MissingExecutable, ClaudeObservation.CaptureState.Unavailable),
            (ClaudeObservation.FailureReason.Timeout, ClaudeObservation.CaptureState.Disconnected),
            (ClaudeObservation.FailureReason.OutputTooLarge, ClaudeObservation.CaptureState.Disconnected),
            (ClaudeObservation.FailureReason.CommandFailed, ClaudeObservation.CaptureState.Disconnected),
        })
        {
            var processes = new ProcessReader(Inventory());
            var source = new ClaudeObservationSource(new Reader(Agent, failure), processes, new Clock());
            var result = await source.CaptureAsync(TestContext.Current.CancellationToken);
            Assert.Equal(expectedState, result.State);
            Assert.Null(result.Sessions);
            Assert.Null(result.ObservedAt);
            Assert.Null(result.ProcessCoverage.UnmatchedCount);
            Assert.Equal(0, processes.Reads);
        }
    }

    [Fact]
    public async Task UnsupportedVersionStopsBeforeAgentQueryOrProcessInventory()
    {
        var reader = new Reader(Agent, version: "999.1.2 (Claude Code)");
        var processes = new ProcessReader(Inventory());
        var result = await new ClaudeObservationSource(reader, processes, new Clock()).CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.CaptureState.UnsupportedVersion, result.State);
        Assert.Equal("999.1.2", result.SourceVersion);
        Assert.Equal(1, reader.Reads);
        Assert.Equal(0, processes.Reads);
    }

    [Fact]
    public async Task CurrentReadOnlyConformanceVersionIsAnExactCompatibilityEntry()
    {
        var current = await Source(new Reader("[]", version: "2.1.322 (Claude Code)"), Inventory())
            .CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.CaptureState.Observed, current.State);
        Assert.Equal("2.1.322", current.SourceVersion);
        var next = await Source(new Reader("[]", version: "2.1.323 (Claude Code)"), Inventory())
            .CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.CaptureState.UnsupportedVersion, next.State);
        Assert.Null(next.Sessions);
    }

    [Theory]
    [InlineData("CANARY")]
    [InlineData("2.1.312 (Claude Code) CANARY")]
    public async Task VersionPayloadNeverEscapesItsFormatBoundary(string version)
    {
        var result = await Source(new Reader(Agent, version: version), Inventory()).CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Null(result.SourceVersion);
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewProcessAfterCaptureBeganDoesNotMatchAStalePid()
    {
        var result = await Source(
            new Reader(Agent),
            Inventory(new ClaudeProcessInventory.ProcessIdentity(123, Now.AddSeconds(1), 0, ClaudeProcessInventory.ProcessRole.Claude, true)))
            .CaptureAsync(TestContext.Current.CancellationToken);
        var session = Assert.Single(result.Sessions!);
        Assert.False(session.ProcessMatched);
        Assert.Equal(ClaudeObservation.WorkState.Unknown, session.State);
        Assert.Equal(1, result.ProcessCoverage.KnownUnmatchedCount);
    }

    [Fact]
    public void ReusedAncestorDoesNotInventDesktopClassification()
    {
        var entry = new ClaudeAgentEntry(Guid.NewGuid(), ClaudeAgentEntry.AgentKind.Interactive, ClaudeObservation.WorkState.Idle, false, 123, false);
        var processes = Inventory(
            new(123, Now.AddMinutes(-1), 50, ClaudeProcessInventory.ProcessRole.Claude, true),
            new(50, Now, 0, ClaudeProcessInventory.ProcessRole.DesktopApp, true));
        var summary = ClaudeObservationSource.Reconcile(entry, processes, Now);
        Assert.Equal(ClaudeObservation.Entrypoint.Unknown, summary.Entrypoint);
    }

    [Fact]
    public void PackagedExecutableAloneIsNotDesktopEvidence()
    {
        var entry = new ClaudeAgentEntry(Guid.NewGuid(), ClaudeAgentEntry.AgentKind.Interactive, ClaudeObservation.WorkState.Idle, false, 123, false);
        var processes = Inventory(
            new(123, Now.AddMinutes(-1), 50, ClaudeProcessInventory.ProcessRole.DesktopApp, true),
            new(50, Now.AddMinutes(-2), 0, ClaudeProcessInventory.ProcessRole.Terminal, false));
        var summary = ClaudeObservationSource.Reconcile(entry, processes, Now);
        Assert.Equal(ClaudeObservation.Entrypoint.InteractiveCli, summary.Entrypoint);
    }

    [Fact]
    public void DeniedIdentityAndUnrelatedPidNeverMatchClaude()
    {
        var entry = new ClaudeAgentEntry(Guid.NewGuid(), ClaudeAgentEntry.AgentKind.Interactive, ClaudeObservation.WorkState.Idle, false, 123, false);
        foreach (var identity in new[]
        {
            new ClaudeProcessInventory.ProcessIdentity(123, null, 0, ClaudeProcessInventory.ProcessRole.Claude, true),
            new ClaudeProcessInventory.ProcessIdentity(123, Now.AddMinutes(-1), 0, ClaudeProcessInventory.ProcessRole.Other, false),
        })
        {
            Assert.False(ClaudeObservationSource.Reconcile(entry, Inventory(identity), Now).ProcessMatched);
        }
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("waiting")]
    [InlineData("idle")]
    public async Task ContradictoryStoppedStatusIsUnknownWithoutAPid(string status)
    {
        var json = $$"""
            [{"sessionId":"26d93c03-641e-4199-a713-f5a32823a171",
            "kind":"background","status":"{{status}}","state":"stopped"}]
            """;
        var result = await Source(new Reader(json), Inventory()).CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.WorkState.Unknown, Assert.Single(result.Sessions!).State);
        Assert.Equal(ClaudeObservation.CaptureState.Partial, result.State);
        Assert.Contains(ClaudeObservation.CoverageGap.SchemaDrift, result.CoverageGaps);
    }

    [Fact]
    public async Task UnexpectedBoundaryExceptionCannotPublishSourceText()
    {
        var result = await new ClaudeObservationSource(new ThrowingReader(), new ProcessReader(Inventory()), new Clock())
            .CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.CaptureState.Disconnected, result.State);
        Assert.Null(result.Sessions);
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static ClaudeObservationSource Source(Reader reader, ClaudeProcessInventory processes) =>
        new(reader, new ProcessReader(processes), new Clock());

    private static ClaudeProcessInventory Inventory(params ClaudeProcessInventory.ProcessIdentity[] identities) =>
        new(true, identities.ToDictionary(identity => identity.ProcessId));

    private sealed class Reader(
        string agents,
        ClaudeObservation.FailureReason failure = ClaudeObservation.FailureReason.None,
        string version = "2.1.312 (Claude Code)") : IClaudeCommandReader
    {
        internal int Reads { get; private set; }

        public Task<ClaudeCommandResult> ReadAsync(ClaudeReadOperation operation, CancellationToken cancellationToken)
        {
            this.Reads++;
            return Task.FromResult(new ClaudeCommandResult(
                failure,
                ClaudeObservation.RootScope.DefaultRoot,
                Encoding.UTF8.GetBytes(operation == ClaudeReadOperation.Version ? version : agents)));
        }
    }

    private sealed class ProcessReader(ClaudeProcessInventory inventory) : IClaudeProcessReader
    {
        internal int Reads { get; private set; }

        public ClaudeProcessInventory Capture()
        {
            this.Reads++;
            return inventory;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ThrowingReader : IClaudeCommandReader
    {
        public Task<ClaudeCommandResult> ReadAsync(ClaudeReadOperation operation, CancellationToken cancellationToken) =>
            throw new IOException("CANARY");
    }
}

namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class CapabilityReportTests
{
    [Fact]
    public void CX1_CodexDesktopIsUnreachableWithNoSupportedEndpointAndAProcessPresenceCount()
    {
        var ledger = new ActivityLedger(Settings, Clock());

        var admitted = ledger.Admit(CodexDesktopReport(ledger.StartGeneration(CodexDesktop)));

        var desktop = Assert.Single(admitted.Entrypoints);
        Assert.Equal(Entrypoint.Desktop, desktop.Entrypoint);
        Assert.Equal(CoverageLevel.Unreachable, desktop.Observation);
        Assert.Equal(GapReason.NoSupportedEndpoint, desktop.Gap);
        Assert.Equal(1, desktop.UnlistedProcessCount);
        Assert.Equal(new[] { Channel.ProcessScan }, desktop.Channels);
    }

    [Fact]
    public void CX4_TwoCodexOwnersSharingOneHomeAreTwoSourcesAndNeitherSeesTheOthersWork()
    {
        Assert.NotEqual(CodexDesktop, CodexOwned);
        var ledger = new ActivityLedger(Settings, Clock());
        var thread = WorkKey.Of(CodexOwned, WorkKind.Thread, "thread-1");
        var server = Process(301);

        ledger.Apply(SafeSourceBatch.Polled(
            CodexOwned, ledger.StartGeneration(CodexOwned), Start, PollEvery, [ListedWork.Entry(thread, Entrypoint.WorkbenchHosted, ListedStatus.Idle, incarnation: server)], [server], true));
        ledger.Apply(SafeSourceBatch.Polled(CodexDesktop, ledger.StartGeneration(CodexDesktop), Start, PollEvery, [], [server], true));

        Assert.Equal(thread, Assert.Single(ledger.Work(CodexOwned)).Key);
        Assert.Empty(ledger.Work(CodexDesktop));
        Assert.Equal(WorkState.Idle, ledger.Find(thread)!.State);
    }

    [Fact]
    public void AReportFromAnUnissuedOrOlderGenerationCannotHideAVersionChange()
    {
        var ledger = new ActivityLedger(Settings, Clock());
        var g1 = ledger.StartGeneration(Claude);
        var elsewhere = new ActivityLedger(Settings, Clock());
        elsewhere.StartGeneration(Claude);
        var unissued = elsewhere.StartGeneration(Claude);
        ledger.Admit(ClaudeReport(g1, "2.1.312"));

        Assert.Throws<ArgumentException>(() => ledger.Admit(ClaudeReport(unissued, "2.1.312")));

        var g2 = ledger.StartGeneration(Claude);
        var changed = ledger.Admit(ClaudeReport(g2, "2.1.323"));
        var late = ledger.Admit(ClaudeReport(g1, "2.1.312"));

        Assert.Same(changed, late);
        Assert.Equal("2.1.323", late.SourceVersion);
        Assert.All(late.Entrypoints.SelectMany(e => e.Controls), c => Assert.True(c.Disabled.HasFlag(DisabledReason.VersionChanged)));
    }

    [Fact]
    public void AVersionChangeDisablesEveryControlForVersionChangedAndKeepsObservation()
    {
        var clock = Clock();
        var ledger = new ActivityLedger(Settings, clock);
        var generation = ledger.StartGeneration(Claude);
        var before = ledger.Admit(ClaudeReport(generation, "2.1.312"));
        Assert.All(before.Entrypoints.SelectMany(e => e.Controls), c => Assert.False(c.Disabled.HasFlag(DisabledReason.VersionChanged)));

        var after = ledger.Admit(ClaudeReport(generation, "2.1.323"));
        var again = ledger.Admit(ClaudeReport(generation, "2.1.323"));

        var controls = after.Entrypoints.SelectMany(e => e.Controls).ToList();
        Assert.Equal(2, controls.Count);
        Assert.All(controls, c => Assert.False(c.Enabled));
        Assert.All(controls, c => Assert.True(c.Disabled.HasFlag(DisabledReason.VersionChanged)));
        Assert.Equal(DisabledReason.NoConditionalDispatch | DisabledReason.VersionChanged, controls[0].Disabled);
        Assert.Equal(DisabledReason.NoSupportedRoute | DisabledReason.VersionChanged, controls[1].Disabled);
        Assert.Equal(before.Entrypoints.Select(e => e.Observation), after.Entrypoints.Select(e => e.Observation));
        Assert.All(again.Entrypoints.SelectMany(e => e.Controls), c => Assert.True(c.Disabled.HasFlag(DisabledReason.VersionChanged)));

        var key = Session(SessionA);
        var live = Process(101);
        clock.Advance(PollEvery);
        Assert.Equal(
            ApplyOutcome.Applied,
            ledger.Apply(SafeSourceBatch.Polled(Claude, generation, clock.GetUtcNow(), PollEvery, [ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live], true)));
        Assert.Equal(WorkState.Idle, ledger.Find(key)!.State);
    }

    [Fact]
    public void VerifiedCoverageNeedsConformanceEvidenceOnTheReportedVersion()
    {
        var generation = new ActivityLedger(Settings, Clock()).StartGeneration(Claude);
        EntrypointCoverage Verified(EvidenceRef evidence) =>
            new(Entrypoint.Background, CoverageLevel.Verified, [Channel.Poll], [WorkKind.Session], [], evidence);

        Assert.Throws<ArgumentException>(() => Verified(EvidenceRef.Research("claude-ticket7")));
        var otherVersion = Verified(EvidenceRef.Conformance("conformance-run-1", "2.1.312"));
        Assert.Throws<ArgumentException>(() => new CapabilityReport(Claude, generation, "2.1.323", SourceState.Connected, [otherVersion], Start));
        Assert.Throws<ArgumentException>(() => new CapabilityReport(Claude, generation, null, SourceState.Connected, [otherVersion], Start));

        var sameVersion = new CapabilityReport(
            Claude, generation, "2.1.323", SourceState.Connected, [Verified(EvidenceRef.Conformance("conformance-run-1", "2.1.323"))], Start);
        Assert.Equal(CoverageLevel.Verified, Assert.Single(sameVersion.Entrypoints).Observation);
    }

    [Fact]
    public void AnUncoveredEntrypointMustNameItsGapAndCoverageNeedsSupportingEvidence()
    {
        Assert.Throws<ArgumentException>(() =>
            new EntrypointCoverage(Entrypoint.Headless, CoverageLevel.Unverified, [], [], [], EvidenceRef.Documentation("claude-cli-reference")));
        Assert.Throws<ArgumentException>(() =>
            new EntrypointCoverage(Entrypoint.Background, CoverageLevel.Observed, [Channel.Poll], [WorkKind.Session], [], EvidenceRef.Documentation("claude-cli-reference")));
        var named = new EntrypointCoverage(
            Entrypoint.Headless, CoverageLevel.Unverified, [], [], [], EvidenceRef.Documentation("claude-cli-reference"), GapReason.AwaitingAuthorization);
        Assert.Equal(GapReason.AwaitingAuthorization, named.Gap);
    }

    [Fact]
    public void NoControlCanBeBuiltEnabled()
    {
        var evidence = EvidenceRef.Research("claude-ticket7");

        Assert.Throws<ArgumentException>(() => ControlCapability.Disable(ControlAction.StopBackgroundSession, WorkKind.Session, DisabledReason.None, evidence));
        Assert.Throws<ArgumentException>(() => ControlCapability.Disable(ControlAction.StopBackgroundSession, WorkKind.Session, (DisabledReason)64, evidence));
        Assert.False(ControlCapability.Disable(ControlAction.StopBackgroundSession, WorkKind.Session, DisabledReason.NoConditionalDispatch, evidence).Enabled);
    }

    internal static CapabilityReport ClaudeReport(SourceGeneration generation, string version) => new(
        Claude,
        generation,
        version,
        SourceState.Connected,
        [
            new EntrypointCoverage(
                Entrypoint.Background,
                CoverageLevel.Observed,
                [Channel.Poll, Channel.ProcessScan],
                [WorkKind.Session],
                [ControlCapability.Disable(ControlAction.StopBackgroundSession, WorkKind.Session, DisabledReason.NoConditionalDispatch, EvidenceRef.Research("claude-ticket7"))],
                EvidenceRef.Research("claude-ticket7")),
            new EntrypointCoverage(
                Entrypoint.Desktop,
                CoverageLevel.Observed,
                [Channel.Poll, Channel.ProcessScan],
                [WorkKind.Session],
                [ControlCapability.Disable(ControlAction.StopBackgroundSession, WorkKind.Session, DisabledReason.NoSupportedRoute, EvidenceRef.Research("claude-ticket7"))],
                EvidenceRef.Research("claude-ticket7")),
            new EntrypointCoverage(
                Entrypoint.Headless, CoverageLevel.Unverified, [], [], [], EvidenceRef.Documentation("claude-cli-reference"), GapReason.AwaitingAuthorization),
        ],
        Start);

    internal static CapabilityReport CodexDesktopReport(SourceGeneration generation) => new(
        CodexDesktop,
        generation,
        null,
        SourceState.Unavailable,
        [
            new EntrypointCoverage(
                Entrypoint.Desktop,
                CoverageLevel.Unreachable,
                [Channel.ProcessScan],
                [],
                [],
                EvidenceRef.Research("codex-connection-diagnosis"),
                GapReason.NoSupportedEndpoint,
                unlistedProcessCount: 1),
        ],
        Start);
}

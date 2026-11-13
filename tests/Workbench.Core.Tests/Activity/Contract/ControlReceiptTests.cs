namespace Workbench.Core.Tests.Activity.Contract;

using System.Reflection;
using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class ControlReceiptTests
{
    private static readonly (ReceiptStage From, ReceiptStage To)[] ContractEdges =
    [
        (ReceiptStage.Requested, ReceiptStage.Refused),
        (ReceiptStage.Requested, ReceiptStage.NoActionNeeded),
        (ReceiptStage.Requested, ReceiptStage.Accepted),
        (ReceiptStage.Accepted, ReceiptStage.Dispatched),
        (ReceiptStage.Dispatched, ReceiptStage.SourceAcknowledged),
        (ReceiptStage.Dispatched, ReceiptStage.SourceRejected),
        (ReceiptStage.Dispatched, ReceiptStage.DeliveryUnknown),
        (ReceiptStage.SourceAcknowledged, ReceiptStage.OutcomeVerified),
        (ReceiptStage.SourceAcknowledged, ReceiptStage.OutcomeNotObserved),
        (ReceiptStage.SourceAcknowledged, ReceiptStage.OutcomeContradicted),
        (ReceiptStage.DeliveryUnknown, ReceiptStage.OutcomeVerified),
        (ReceiptStage.DeliveryUnknown, ReceiptStage.OutcomeNotObserved),
        (ReceiptStage.DeliveryUnknown, ReceiptStage.OutcomeContradicted),
        (ReceiptStage.SourceRejected, ReceiptStage.OutcomeVerified),
        (ReceiptStage.SourceRejected, ReceiptStage.OutcomeNotObserved),
    ];

    private static readonly Dictionary<ReceiptStage, ReceiptStage[]> PathTo = new()
    {
        [ReceiptStage.Requested] = [],
        [ReceiptStage.Refused] = [ReceiptStage.Refused],
        [ReceiptStage.NoActionNeeded] = [ReceiptStage.NoActionNeeded],
        [ReceiptStage.Accepted] = [ReceiptStage.Accepted],
        [ReceiptStage.Dispatched] = [ReceiptStage.Accepted, ReceiptStage.Dispatched],
        [ReceiptStage.SourceAcknowledged] = [ReceiptStage.Accepted, ReceiptStage.Dispatched, ReceiptStage.SourceAcknowledged],
        [ReceiptStage.SourceRejected] = [ReceiptStage.Accepted, ReceiptStage.Dispatched, ReceiptStage.SourceRejected],
        [ReceiptStage.DeliveryUnknown] = [ReceiptStage.Accepted, ReceiptStage.Dispatched, ReceiptStage.DeliveryUnknown],
        [ReceiptStage.OutcomeVerified] = [ReceiptStage.Accepted, ReceiptStage.Dispatched, ReceiptStage.SourceAcknowledged, ReceiptStage.OutcomeVerified],
        [ReceiptStage.OutcomeNotObserved] = [ReceiptStage.Accepted, ReceiptStage.Dispatched, ReceiptStage.SourceAcknowledged, ReceiptStage.OutcomeNotObserved],
        [ReceiptStage.OutcomeContradicted] = [ReceiptStage.Accepted, ReceiptStage.Dispatched, ReceiptStage.SourceAcknowledged, ReceiptStage.OutcomeContradicted],
    };

    [Fact]
    public void AReceiptMovesOnlyAlongTheContractsStageEdges()
    {
        var allowed = 0;
        foreach (var from in Enum.GetValues<ReceiptStage>())
        {
            foreach (var to in Enum.GetValues<ReceiptStage>())
            {
                var receipt = Reach(from);
                var refusal = to == ReceiptStage.Refused ? RefusalReason.NoConditionalDispatch : (RefusalReason?)null;
                if (ContractEdges.Contains((from, to)))
                {
                    Assert.Equal(to, receipt.Advance(to, Start, refusal).Stage);
                    allowed++;
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => receipt.Advance(to, Start, refusal));
                }
            }
        }

        Assert.Equal(ContractEdges.Length, allowed);
    }

    [Fact]
    public void CT4_ADuplicateRequestIdReturnsTheFirstReceiptAndDispatchesNothing()
    {
        var scenario = OwnedIdleBackgroundSession(out var key, out var live);
        var id = new RequestId(Guid.Parse("00000000-0000-4000-8000-0000000000c4"));

        var first = scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator);
        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator);

        Assert.Same(first, second);
        Assert.Equal((ReceiptStage.Refused, RefusalReason.NoConditionalDispatch), (first.Stage, first.Refusal));
        Assert.Equal(new[] { ReceiptStage.Requested, ReceiptStage.Refused }, first.History.Select(step => step.Stage));
        Assert.False(first.WorkbenchDispatched);
    }

    [Fact]
    public void ADuplicateRequestIdReturnsTheReceiptOnlyToItsOwnCallerScopeOrTheOperator()
    {
        var scenario = OwnedIdleBackgroundSession(out var key, out var live);
        var operatorId = new RequestId(Guid.Parse("00000000-0000-4000-8000-0000000000c6"));
        var droneId = new RequestId(Guid.Parse("00000000-0000-4000-8000-0000000000c7"));
        var operatorReceipt = scenario.Ledger.Request(operatorId, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator);
        var droneReceipt = scenario.Ledger.Request(droneId, ControlAction.StopBackgroundSession, key, live, CallerScope.Drone);

        var droneReusingOperatorId = scenario.Ledger.Request(operatorId, ControlAction.StopBackgroundSession, key, live, CallerScope.Drone);

        Assert.NotSame(operatorReceipt, droneReusingOperatorId);
        Assert.Equal((ReceiptStage.Refused, RefusalReason.CallerScope), (droneReusingOperatorId.Stage, droneReusingOperatorId.Refusal));
        Assert.False(droneReusingOperatorId.WorkbenchDispatched);
        Assert.Same(operatorReceipt, scenario.Ledger.Request(operatorId, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator));
        Assert.Same(droneReceipt, scenario.Ledger.Request(droneId, ControlAction.StopBackgroundSession, key, live, CallerScope.Drone));
        Assert.Same(droneReceipt, scenario.Ledger.Request(droneId, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator));
    }

    [Fact]
    public void AnIdReusedForOtherWorkIsRefusedAsAConflictAndLeavesTheFirstReceipt()
    {
        var scenario = OwnedIdleBackgroundSession(out var key, out var live);
        var id = new RequestId(Guid.Parse("00000000-0000-4000-8000-0000000000c5"));
        var first = scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator);

        var conflict = scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, Session(SessionB), null, CallerScope.Operator);

        Assert.NotSame(first, conflict);
        Assert.Equal((ReceiptStage.Refused, RefusalReason.RequestIdConflict), (conflict.Stage, conflict.Refusal));
        Assert.Same(first, scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator));
    }

    [Fact]
    public void CX2_AReconnectBeforeAnyNewBatchMakesTheOldCommandUnknownAndRefusesItsRequestAsStale()
    {
        var scenario = new LedgerScenario(CodexOwned);
        var server = Process(301);
        var command = WorkKey.Command(CodexOwned, scenario.Generation, "proc-7");
        Assert.True(ControlReference.TryConnectionScoped(command, "proc-7", out var reference));
        scenario.Poll(
            [ListedWork.Entry(command, Entrypoint.Headless, ListedStatus.Busy, incarnation: server, ownership: OwnershipEvidence.WorkbenchOwnedServer, reference: reference)],
            [server]);
        scenario.Ledger.Admit(CodexOwnedReport(scenario.Generation));
        var beforeReconnect = scenario.Ledger.Request(NewId(), ControlAction.TerminateOwnedCommand, command, server, CallerScope.Operator);

        var reconnected = scenario.Ledger.StartGeneration(CodexOwned);
        var afterReconnect = scenario.Ledger.Request(NewId(), ControlAction.TerminateOwnedCommand, command, server, CallerScope.Operator);

        Assert.Equal(RefusalReason.NoConformance, beforeReconnect.Refusal);
        Assert.Equal(reconnected, scenario.Ledger.Source(CodexOwned)!.Generation);
        Assert.Equal((WorkState.Unknown, ReasonCode.ConnectionLost), scenario.StateOf(command));
        Assert.False(reference.IsUsableAt(reconnected));
        Assert.Equal((ReceiptStage.Refused, RefusalReason.StaleTarget), (afterReconnect.Stage, afterReconnect.Refusal));
    }

    [Fact]
    public void ARestartBeforeAnyNewBatchRefusesTheAlreadyStoppedNoOp()
    {
        var scenario = OwnedIdleBackgroundSession(out var key, out _);
        scenario.Poll([Stopped(scenario, key)], []);
        Assert.Equal(ReceiptStage.NoActionNeeded, scenario.Ledger.Request(NewId(), ControlAction.StopBackgroundSession, key, null, CallerScope.Operator).Stage);

        scenario.Ledger.StartGeneration(Claude);

        AssertRefused(scenario, key, RefusalReason.NoConditionalDispatch);
    }

    [Fact]
    public void CX3_AFreshOwnedStoppedTargetIsNoActionNeededWithZeroDispatch()
    {
        var scenario = OwnedIdleBackgroundSession(out var key, out _);
        scenario.Poll([Stopped(scenario, key)], []);

        var receipt = scenario.Ledger.Request(NewId(), ControlAction.StopBackgroundSession, key, null, CallerScope.Operator);

        Assert.Equal(ReceiptStage.NoActionNeeded, receipt.Stage);
        Assert.Equal(new[] { ReceiptStage.Requested, ReceiptStage.NoActionNeeded }, receipt.History.Select(step => step.Stage));
        Assert.Null(receipt.Refusal);
        Assert.False(receipt.WorkbenchDispatched);
    }

    [Fact]
    public void CX3_ProviderRepeatControlFactsKeepTheirOwnClassificationAndCannotEnterAReceipt()
    {
        var claudeRepeatStop = ControlDispatch.Acknowledged(Start);
        var codexRepeatTerminate = ControlDispatch.Rejected(SourceRejection.NoActiveCommand, Start);

        Assert.Equal((ReceiptStage.SourceAcknowledged, (SourceRejection?)null), (claudeRepeatStop.Stage, claudeRepeatStop.Rejection));
        Assert.Equal((ReceiptStage.SourceRejected, SourceRejection.NoActiveCommand), (codexRepeatTerminate.Stage, codexRepeatTerminate.Rejection));
        var ledgerInputs = typeof(ActivityLedger).GetMethods(BindingFlags.Public | BindingFlags.Instance).SelectMany(m => m.GetParameters()).Select(p => p.ParameterType);
        Assert.DoesNotContain(typeof(ControlDispatch), ledgerInputs);
        Assert.DoesNotContain(typeof(ControlEvidence), ledgerInputs);
    }

    [Fact]
    public void StaleUnownedOrStillLiveStoppedEvidenceNeverYieldsAlreadyStopped()
    {
        var stale = OwnedIdleBackgroundSession(out var staleKey, out _);
        stale.Poll([Stopped(stale, staleKey)], []);
        stale.Clock.Advance(Settings.StaleAfter + TimeSpan.FromSeconds(1));
        AssertRefused(stale, staleKey, RefusalReason.StaleTarget);

        var unowned = new LedgerScenario(Claude);
        var unownedKey = Session(SessionA);
        Assert.True(ControlReference.TrySourceStable(unownedKey, ShortA, out var unownedReference));
        unowned.Poll([ListedWork.Entry(unownedKey, Entrypoint.Background, lifecycle: SourceLifecycle.Stopped, reference: unownedReference)], []);
        AssertRefused(unowned, unownedKey, RefusalReason.NoOwnershipEvidence);

        var stillLive = OwnedIdleBackgroundSession(out var stillLiveKey, out var process);
        stillLive.Poll([Stopped(stillLive, stillLiveKey)], [process]);
        Assert.Equal(WorkState.Stopped, stillLive.View(stillLiveKey).State);
        AssertRefused(stillLive, stillLiveKey, RefusalReason.IncarnationChanged);
    }

    [Fact]
    public void TheGateRefusesEachRequestWithTheReasonTheContractNames()
    {
        var scenario = OwnedIdleBackgroundSession(out var owned, out var live);
        var interactive = Session(SessionB);
        var unowned = Session(SessionC);
        var other = Process(102);
        var third = Process(103);
        Assert.True(ControlReference.TrySourceStable(unowned, "5e55a003", out var unownedReference));
        scenario.Poll(
            [
                ListedWork.Entry(owned, Entrypoint.Background, ListedStatus.Idle, incarnation: live, reference: scenario.View(owned).Reference),
                ListedWork.Entry(interactive, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: other),
                ListedWork.Entry(unowned, Entrypoint.Background, ListedStatus.Idle, incarnation: third, reference: unownedReference),
            ],
            [live, other, third]);

        Assert.Equal(RefusalReason.CallerScope, Refusal(scenario, owned, live, CallerScope.Drone));
        Assert.Equal(RefusalReason.UnknownTarget, Refusal(scenario, Session("5e55a0ff-0000-4000-8000-0000000000ff"), live));
        Assert.Equal(RefusalReason.NoSupportedRoute, Refusal(scenario, interactive, other));
        Assert.Equal(RefusalReason.NoOwnershipEvidence, Refusal(scenario, unowned, third));
        Assert.Equal(RefusalReason.IncarnationChanged, Refusal(scenario, owned, other));
        Assert.Equal(RefusalReason.NoConditionalDispatch, Refusal(scenario, owned, live));

        scenario.Ledger.Admit(CapabilityReportTests.ClaudeReport(scenario.Generation, "2.1.323"));
        Assert.Equal(RefusalReason.VersionChanged, Refusal(scenario, owned, live));
    }

    [Fact]
    public void OwnershipCountsOnlyWhenItsKindFitsTheWorksSourceAndEntrypoint()
    {
        var claude = new LedgerScenario(Claude);
        var session = Session(SessionA);
        var live = Process(101);
        Assert.True(ControlReference.TrySourceStable(session, ShortA, out var sessionReference));
        claude.Poll(
            [ListedWork.Entry(session, Entrypoint.Background, ListedStatus.Idle, incarnation: live, ownership: OwnershipEvidence.WorkbenchOwnedServer, reference: sessionReference)],
            [live]);
        claude.Ledger.Admit(CapabilityReportTests.ClaudeReport(claude.Generation, "2.1.312"));

        Assert.Equal(RefusalReason.NoOwnershipEvidence, Refusal(claude, session, live));
        Assert.Equal(RefusalReason.NoOwnershipEvidence, TerminateRefusal(Entrypoint.Headless, OwnershipEvidence.SameUserBackgroundListing));
        Assert.Equal(RefusalReason.NoOwnershipEvidence, TerminateRefusal(Entrypoint.Desktop, OwnershipEvidence.WorkbenchOwnedServer));
        Assert.Equal(RefusalReason.NoConformance, TerminateRefusal(Entrypoint.Headless, OwnershipEvidence.WorkbenchOwnedServer));

        static RefusalReason? TerminateRefusal(Entrypoint entrypoint, OwnershipEvidence ownership)
        {
            var codex = new LedgerScenario(CodexOwned);
            var server = Process(301);
            var command = WorkKey.Command(CodexOwned, codex.Generation, "proc-7");
            Assert.True(ControlReference.TryConnectionScoped(command, "proc-7", out var reference));
            var usable = entrypoint == Entrypoint.Desktop ? null : reference;
            codex.Poll([ListedWork.Entry(command, entrypoint, ListedStatus.Busy, incarnation: server, ownership: ownership, reference: usable)], [server]);
            codex.Ledger.Admit(CodexOwnedReport(codex.Generation, entrypoint));
            return codex.Ledger.Request(NewId(), ControlAction.TerminateOwnedCommand, command, server, CallerScope.Operator).Refusal;
        }
    }

    [Fact]
    public void OwnershipFromAnEndedLifeDoesNotCarryIntoTheNextLife()
    {
        var scenario = OwnedIdleBackgroundSession(out var key, out _);
        scenario.Poll([Stopped(scenario, key)], []);
        scenario.Poll([], []);
        Assert.Equal(WorkState.Ended, scenario.View(key).State);
        var resumed = Process(102, startedMinutesBefore: 0);
        Assert.True(ControlReference.TrySourceStable(key, ShortA, out var reference));

        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: resumed, reference: reference)], [resumed]);

        Assert.Equal(OwnershipEvidence.None, scenario.View(key).Ownership);
        Assert.Equal(RefusalReason.NoOwnershipEvidence, Refusal(scenario, key, resumed));
    }

    internal static LedgerScenario OwnedIdleBackgroundSession(out WorkKey key, out ProcessIncarnation live)
    {
        var scenario = new LedgerScenario(Claude);
        key = Session(SessionA);
        live = Process(101);
        Assert.True(ControlReference.TrySourceStable(key, ShortA, out var reference));
        scenario.Poll(
            [ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live, ownership: OwnershipEvidence.SameUserBackgroundListing, reference: reference)],
            [live]);
        scenario.Ledger.Admit(CapabilityReportTests.ClaudeReport(scenario.Generation, "2.1.312"));
        return scenario;
    }

    internal static RequestId NewId() => new(Guid.NewGuid());

    internal static ListedWork Stopped(LedgerScenario scenario, WorkKey key) =>
        ListedWork.Entry(key, Entrypoint.Background, lifecycle: SourceLifecycle.Stopped, reference: scenario.View(key).Reference);

    private static RefusalReason? Refusal(LedgerScenario scenario, WorkKey key, ProcessIncarnation seen, CallerScope caller = CallerScope.Operator) =>
        scenario.Ledger.Request(NewId(), ControlAction.StopBackgroundSession, key, seen, caller).Refusal;

    private static void AssertRefused(LedgerScenario scenario, WorkKey key, RefusalReason reason)
    {
        var receipt = scenario.Ledger.Request(NewId(), ControlAction.StopBackgroundSession, key, null, CallerScope.Operator);
        Assert.Equal((ReceiptStage.Refused, reason), (receipt.Stage, receipt.Refusal));
    }

    private static ControlReceipt Reach(ReceiptStage stage) => PathTo[stage].Aggregate(
        new ControlReceipt(NewId(), ControlAction.StopBackgroundSession, Session(SessionA), null, CallerScope.Operator, Start),
        (receipt, next) => receipt.Advance(next, Start, next == ReceiptStage.Refused ? RefusalReason.NoConditionalDispatch : null));

    private static CapabilityReport CodexOwnedReport(SourceGeneration generation, Entrypoint entrypoint = Entrypoint.Headless) => new(
        CodexOwned,
        generation,
        "0.167.0-alpha.4.1",
        SourceState.Connected,
        [
            new EntrypointCoverage(
                entrypoint,
                CoverageLevel.Partial,
                [Channel.Poll],
                [WorkKind.Command],
                [ControlCapability.Disable(ControlAction.TerminateOwnedCommand, WorkKind.Command, DisabledReason.NoConformance, EvidenceRef.Research("codex-ticket7"))],
                EvidenceRef.Research("codex-ticket7")),
        ],
        Start);
}

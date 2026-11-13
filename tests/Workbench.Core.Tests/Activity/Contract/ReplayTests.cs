namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

// One named replay per CA1 live lane. Each writes its result lines so a lane run can save them. Lane 1 is the whole suite at trunk and head.
public sealed class ReplayTests(ITestOutputHelper output)
{
    [Fact]
    public void Lane2_ClaudeLifecycle()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var first = Process(101);
        var respawned = Process(102, startedMinutesBefore: 0);
        var observed = new List<WorkState>();
        void Step(string label, ListedWork entry, ProcessIncarnation[] live)
        {
            scenario.Poll([entry], live);
            var view = this.Write(label, scenario, key);
            Assert.Equal(key, view.Key);
            observed.Add(view.State);
        }

        Step("start", ListedWork.Entry(key, Entrypoint.Background), []);
        Step("idle", ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, SourceLifecycle.Working, first), [first]);
        Step("kill", ListedWork.Entry(key, Entrypoint.Background, lifecycle: SourceLifecycle.Blocked), []);
        Step("respawn", ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, SourceLifecycle.Working, respawned), [respawned]);
        Step("stop", ListedWork.Entry(key, Entrypoint.Background, lifecycle: SourceLifecycle.Stopped), []);

        Assert.Equal(new[] { WorkState.Starting, WorkState.Idle, WorkState.Restarting, WorkState.Idle, WorkState.Stopped }, observed);
        Assert.Single(scenario.Ledger.Work(Claude));
        output.WriteLine($"PASS one work key across {observed.Count} samples");
    }

    [Fact]
    public void Lane3_LateBatch()
    {
        var scenario = new LedgerScenario(Claude);
        var live = Process(101);
        var older = scenario.Generation;
        scenario.Poll([ListedWork.Entry(Session(SessionA), Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        scenario.Generation = scenario.Ledger.StartGeneration(Claude);
        scenario.Poll(
            [
                ListedWork.Entry(Session(SessionA), Entrypoint.Background, ListedStatus.Idle, incarnation: live),
                ListedWork.Entry(Session(SessionB), Entrypoint.Background),
            ],
            [live]);
        var before = scenario.Ledger.Work(Claude);

        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        var outcome = scenario.Apply(SafeSourceBatch.Polled(Claude, older, scenario.Now, [], [], processInventoryComplete: true));

        output.WriteLine($"late {older} batch after {scenario.Generation}: {outcome}");
        Assert.Equal(ApplyOutcome.IgnoredOlderGeneration, outcome);
        Assert.Equal(before, scenario.Ledger.Work(Claude));
        output.WriteLine($"PASS projection unchanged ({before.Count} items)");
    }

    [Fact]
    public void Lane4_Disconnect()
    {
        var scenario = new LedgerScenario(Claude);
        var keys = new[] { Session(SessionA), Session(SessionB), Session(SessionC) };
        var processes = new[] { Process(101), Process(102), Process(103) };
        ListedStatus[] statuses = [ListedStatus.Idle, ListedStatus.Busy, ListedStatus.Waiting];
        scenario.Poll([.. keys.Select((key, i) => ListedWork.Entry(key, Entrypoint.Background, statuses[i], incarnation: processes[i]))], processes);

        scenario.Fail(DisconnectReason.Timeout);

        var source = scenario.Ledger.Source(Claude)!;
        output.WriteLine($"source {source.State}/{source.Disconnect}");
        Assert.Equal((SourceState.Disconnected, DisconnectReason.Timeout), (source.State, source.Disconnect));
        foreach (var key in keys)
        {
            var view = this.Write("after failure", scenario, key);
            Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected), (view.State, view.Reason));
        }

        Assert.DoesNotContain(scenario.Ledger.Work(Claude), view => view.State == WorkState.Ended);
        output.WriteLine("PASS three items Stale/SourceDisconnected, none Ended");
    }

    [Fact]
    public void Lane5_PidReuse()
    {
        var scenario = new LedgerScenario(Claude);
        var original = Session(SessionA);
        var unrelated = Session(SessionB);
        var exited = new ProcessIncarnation(4321, Start.AddMinutes(-10));
        var reused = new ProcessIncarnation(4321, Start.AddSeconds(5));
        scenario.Poll([ListedWork.Entry(original, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: exited)], [exited]);
        scenario.Poll([], []);
        this.Write("exited", scenario, original);

        scenario.Poll([ListedWork.Entry(unrelated, Entrypoint.InteractiveCli, ListedStatus.Busy, incarnation: reused)], [reused]);

        var old = this.Write("after reuse", scenario, original);
        this.Write("reusing process", scenario, unrelated);
        Assert.Equal((WorkState.Ended, ReasonCode.ProcessExited), (old.State, old.Reason));
        Assert.Equal(new[] { exited }, old.Incarnations.Select(association => association.Incarnation));
        Assert.Equal(new[] { reused }, scenario.View(unrelated).Incarnations.Select(association => association.Incarnation));
        output.WriteLine("PASS old key stays Ended and gains no association");
    }

    [Fact]
    public void Lane6_DuplicateRequest()
    {
        var scenario = ControlReceiptTests.OwnedIdleBackgroundSession(out var key, out var live);
        var id = new RequestId(Guid.Parse("00000000-0000-4000-8000-0000000000a6"));

        var first = scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator);
        var second = scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, live, CallerScope.Operator);

        this.Write("first", first);
        this.Write("second", second);
        Assert.Same(first, second);
        Assert.False(first.WorkbenchDispatched);
        output.WriteLine("PASS one receipt, zero dispatch (CA1 default: every control is disabled)");
    }

    [Fact]
    public void Lane7_CodexReconnect()
    {
        var scenario = new LedgerScenario(CodexOwned);
        var server = Process(301);
        var command = WorkKey.Command(CodexOwned, scenario.Generation, "proc-7");
        Assert.True(ControlReference.TryConnectionScoped(command, "proc-7", out var reference));
        scenario.Poll(
            [ListedWork.Entry(command, Entrypoint.Headless, ListedStatus.Busy, incarnation: server, ownership: OwnershipEvidence.WorkbenchOwnedServer, reference: reference)],
            [server]);
        this.Write("connected", scenario, command);

        scenario.Generation = scenario.Ledger.StartGeneration(CodexOwned);
        scenario.Poll([], [server]);
        var receipt = scenario.Ledger.Request(ControlReceiptTests.NewId(), ControlAction.TerminateOwnedCommand, command, server, CallerScope.Operator);

        this.Write("reconnected", scenario, command);
        this.Write("terminate", receipt);
        Assert.False(reference.IsUsableAt(scenario.Generation));
        Assert.Equal((ReceiptStage.Refused, RefusalReason.StaleTarget), (receipt.Stage, receipt.Refusal));
        output.WriteLine("PASS connection-scoped reference rejected as stale");
    }

    [Fact]
    public void Lane8_RepeatControl()
    {
        var scenario = ControlReceiptTests.OwnedIdleBackgroundSession(out var key, out _);
        scenario.Poll([ControlReceiptTests.Stopped(scenario, key)], []);
        var claudeRepeatStop = ControlDispatch.Acknowledged(scenario.Now);
        var codexRepeatTerminate = ControlDispatch.Rejected(SourceRejection.NoActiveCommand, scenario.Now);

        var receipt = scenario.Ledger.Request(ControlReceiptTests.NewId(), ControlAction.StopBackgroundSession, key, null, CallerScope.Operator);

        this.Write("workbench stop on stopped work", receipt);
        output.WriteLine($"provider claude repeat stop: {claudeRepeatStop.Stage}");
        output.WriteLine($"provider codex repeat terminate: {codexRepeatTerminate.Stage}/{codexRepeatTerminate.Rejection}");
        Assert.Equal(ReceiptStage.NoActionNeeded, receipt.Stage);
        Assert.False(receipt.WorkbenchDispatched);
        Assert.Equal(ReceiptStage.SourceAcknowledged, claudeRepeatStop.Stage);
        Assert.Equal((ReceiptStage.SourceRejected, SourceRejection.NoActiveCommand), (codexRepeatTerminate.Stage, codexRepeatTerminate.Rejection));
        output.WriteLine("PASS already stopped with zero dispatch; provider facts keep their classification");
    }

    [Fact]
    public void Lane9_VersionChange()
    {
        var scenario = new LedgerScenario(Claude);
        var before = scenario.Ledger.Admit(CapabilityReportTests.ClaudeReport(scenario.Generation, "2.1.312"));
        scenario.Generation = scenario.Ledger.StartGeneration(Claude);

        var after = scenario.Ledger.Admit(CapabilityReportTests.ClaudeReport(scenario.Generation, "2.1.323"));
        var live = Process(101);
        var applied = scenario.Poll([ListedWork.Entry(Session(SessionA), Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);

        foreach (var coverage in after.Entrypoints)
        {
            output.WriteLine($"{coverage.Entrypoint}: {coverage.Observation}; controls {(coverage.Controls.IsEmpty ? "none" : string.Join("; ", coverage.Controls.Select(c => $"{c.Action} Enabled={c.Enabled} Disabled={c.Disabled}")))}");
        }

        Assert.All(after.Entrypoints.SelectMany(e => e.Controls), c => Assert.True(!c.Enabled && c.Disabled.HasFlag(DisabledReason.VersionChanged)));
        Assert.Equal(before.Entrypoints.Select(e => e.Observation), after.Entrypoints.Select(e => e.Observation));
        Assert.Equal(ApplyOutcome.Applied, applied);
        Assert.Equal(WorkState.Idle, scenario.View(Session(SessionA)).State);
        output.WriteLine("PASS controls disabled for VersionChanged; observation continues");
    }

    [Fact]
    public void Lane10_CodexDesktop()
    {
        var ledger = new ActivityLedger(Settings, Clock());

        var desktop = Assert.Single(ledger.Admit(CapabilityReportTests.CodexDesktopReport(ledger.StartGeneration(CodexDesktop))).Entrypoints);

        output.WriteLine($"{desktop.Entrypoint}: {desktop.Observation}/{desktop.Gap}, unlisted processes {desktop.UnlistedProcessCount}");
        Assert.Equal((CoverageLevel.Unreachable, GapReason.NoSupportedEndpoint), (desktop.Observation, desktop.Gap));
        output.WriteLine("PASS desktop Unreachable with NoSupportedEndpoint");
    }

    private WorkView Write(string label, LedgerScenario scenario, WorkKey key)
    {
        var view = scenario.View(key);
        output.WriteLine($"t+{(scenario.Now - Start).TotalSeconds}s {label}: {view.Key} {view.State}/{view.Reason} incarnations={view.Incarnations.Length}");
        return view;
    }

    private void Write(string label, ControlReceipt receipt) =>
        output.WriteLine($"{label}: {string.Join(" -> ", receipt.History.Select(step => step.Stage))} refusal={receipt.Refusal?.ToString() ?? "none"} dispatched={receipt.WorkbenchDispatched}");
}

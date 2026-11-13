namespace Workbench.Core.Tests.Activity.Contract;

using System.Runtime.CompilerServices;
using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

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
        var outcome = scenario.Apply(SafeSourceBatch.Polled(Claude, older, scenario.Now, PollEvery, [], [], processInventoryComplete: true));

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
        WorkState[] polled = [WorkState.Idle, WorkState.Busy, WorkState.Waiting];
        for (var failed = 1; failed < Settings.MissedPollLimit; failed++)
        {
            scenario.Fail(DisconnectReason.Timeout);
        }

        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal(polled, keys.Select(key => this.Write($"after {Settings.MissedPollLimit - 1} failed polls", scenario, key).State));

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
        var bystander = Session(SessionB);
        var exited = new ProcessIncarnation(4321, Start.AddMinutes(-10));
        var reused = new ProcessIncarnation(4321, Start.AddSeconds(5));
        var bystanderProcess = Process(102);
        var bystanderEntry = ListedWork.Entry(bystander, Entrypoint.InteractiveCli, ListedStatus.Busy, incarnation: bystanderProcess);
        scenario.Poll([ListedWork.Entry(original, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: exited), bystanderEntry], [exited, bystanderProcess]);
        this.Write("listed", scenario, original);

        scenario.Poll([bystanderEntry], [reused, bystanderProcess]);

        var old = this.Write("dropped while its pid runs a new process", scenario, original);
        this.Write("bystander", scenario, bystander);
        Assert.Equal((WorkState.Ended, ReasonCode.ProcessExited), (old.State, old.Reason));
        Assert.Equal(new[] { exited }, old.Incarnations.Select(association => association.Incarnation));
        Assert.Equal(new[] { bystanderProcess }, scenario.View(bystander).Incarnations.Select(association => association.Incarnation));
        Assert.DoesNotContain(scenario.Ledger.Work(Claude), view => view.Incarnations.Any(association => association.Incarnation == reused));
        output.WriteLine("PASS old key Ended/ProcessExited in the reuse poll; no work gained the reused incarnation");
    }

    [Fact]
    public async Task Lane6_DuplicateRequest()
    {
        var clock = Clock();
        var ledger = new ActivityLedger(Settings, clock);
        var adapter = new CountingAdapter(ledger.StartGeneration(Claude), clock);
        var cancellation = TestContext.Current.CancellationToken;
        ledger.Admit(await adapter.DescribeAsync(cancellation));
        await foreach (var batch in adapter.ObserveAsync(cancellation))
        {
            Assert.Equal(ApplyOutcome.Applied, ledger.Apply(batch));
        }

        var id = new RequestId(Guid.Parse("00000000-0000-4000-8000-0000000000a6"));
        var first = await RequestThroughActivity(ledger, adapter, id, CountingAdapter.Key, CountingAdapter.Live, cancellation);
        var second = await RequestThroughActivity(ledger, adapter, id, CountingAdapter.Key, CountingAdapter.Live, cancellation);

        this.Write("first", first);
        this.Write("second", second);
        Assert.Same(first, second);
        Assert.False(first.WorkbenchDispatched);
        Assert.Equal(0, adapter.Dispatches);
        output.WriteLine(
            $"PASS one receipt, {adapter.Dispatches} adapter dispatches. The plan's lane 6 text says one dispatch, which conflicts with CT-4 " +
            "(the first receipt, nothing dispatched). CA1 follows CT-4.");
    }

    [Fact]
    public void Lane7_CodexReconnect()
    {
        var scenario = new LedgerScenario(CodexOwned);
        var server = Process(301);
        var command = WorkKey.Command(CodexOwned, scenario.Generation, "proc-7");
        Assert.True(ControlReference.TryConnectionScoped(command, "proc-7", out var reference));
        scenario.Poll(
            [ListedWork.Entry(command, Entrypoint.WorkbenchHosted, ListedStatus.Busy, incarnation: server, ownership: OwnershipEvidence.WorkbenchOwnedServer, reference: reference)],
            [server]);
        this.Write("connected", scenario, command);

        scenario.Generation = scenario.Ledger.StartGeneration(CodexOwned);
        scenario.Ledger.Admit(ControlReceiptTests.CodexOwnedReport(scenario.Generation));
        var renewed = WorkKey.Command(CodexOwned, scenario.Generation, "proc-7");
        Assert.True(ControlReference.TryConnectionScoped(renewed, "proc-7", out var renewedReference));
        ListedWork Renewed(ControlReference? handle) =>
            ListedWork.Entry(renewed, Entrypoint.WorkbenchHosted, ListedStatus.Busy, incarnation: server, ownership: OwnershipEvidence.WorkbenchOwnedServer, reference: handle);

        scenario.Poll([Renewed(null)], [server]);
        var oldCommand = Terminate(scenario, command, server);
        var withoutReference = Terminate(scenario, renewed, server);
        this.Write("old key after reconnect", scenario, command);
        this.Write("renewed key without reference", scenario, renewed);
        scenario.Poll([Renewed(renewedReference)], [server]);
        var withReference = Terminate(scenario, renewed, server);

        this.Write("terminate old key", oldCommand);
        this.Write("terminate renewed key, no reference", withoutReference);
        this.Write("terminate renewed key, current reference", withReference);
        Assert.False(reference.IsUsableAt(scenario.Generation));
        Assert.Equal((ReceiptStage.Refused, RefusalReason.StaleTarget), (oldCommand.Stage, oldCommand.Refusal));
        Assert.Equal((WorkState.Busy, ReasonCode.SourceReported), scenario.StateOf(renewed));
        Assert.Equal((ReceiptStage.Refused, RefusalReason.StaleTarget), (withoutReference.Stage, withoutReference.Refusal));
        Assert.Equal((ReceiptStage.Refused, RefusalReason.NoConformance), (withReference.Stage, withReference.Refusal));
        output.WriteLine("PASS old reference rejected as stale; fresh live work without a usable reference refused by the reference rule alone");
    }

    [Fact]
    public void Lane8_RepeatControl()
    {
        var scenario = ControlReceiptTests.OwnedIdleBackgroundSession(out var key, out _);
        scenario.Poll([ControlReceiptTests.Stopped(scenario, key)], []);
        var claudeRepeatStop = ControlDispatch.Acknowledged(scenario.Now);
        var codexRepeatTerminate = ControlDispatch.Rejected(SourceRejection.NoActiveCommand, scenario.Now);
        var id = ControlReceiptTests.NewId();

        ControlReceipt[] produced =
        [
            scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, null, CallerScope.Operator),
            scenario.Ledger.Request(id, ControlAction.StopBackgroundSession, key, null, CallerScope.Operator),
            scenario.Ledger.Request(ControlReceiptTests.NewId(), ControlAction.StopBackgroundSession, key, null, CallerScope.Operator),
        ];

        foreach (var receipt in produced)
        {
            this.Write("workbench stop on stopped work", receipt);
        }

        output.WriteLine($"provider claude repeat stop: {claudeRepeatStop.Stage}");
        output.WriteLine($"provider codex repeat terminate: {codexRepeatTerminate.Stage}/{codexRepeatTerminate.Rejection}");
        Assert.All(produced, receipt => Assert.Equal(ReceiptStage.NoActionNeeded, receipt.Stage));
        Assert.All(produced, receipt => Assert.False(receipt.WorkbenchDispatched));
        Assert.DoesNotContain(
            produced.SelectMany(receipt => receipt.History),
            step => step.Stage is ReceiptStage.Dispatched or ReceiptStage.SourceAcknowledged or ReceiptStage.SourceRejected);
        var surface = ControlReceiptTests.LedgerSurfaceTypes();
        Assert.DoesNotContain(typeof(ControlDispatch), surface);
        Assert.DoesNotContain(typeof(ControlEvidence), surface);
        Assert.Equal(ReceiptStage.SourceAcknowledged, claudeRepeatStop.Stage);
        Assert.Equal((ReceiptStage.SourceRejected, SourceRejection.NoActiveCommand), (codexRepeatTerminate.Stage, codexRepeatTerminate.Rejection));
        output.WriteLine(
            $"PASS {produced.Length} ledger receipts, all NoActionNeeded with no Dispatched, SourceAcknowledged or SourceRejected stage; " +
            "no public ledger member accepts a provider fact");
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

    private static ControlReceipt Terminate(LedgerScenario scenario, WorkKey command, ProcessIncarnation seen) =>
        scenario.Ledger.Request(ControlReceiptTests.NewId(), ControlAction.TerminateOwnedCommand, command, seen, CallerScope.Operator);

    private static async Task<ControlReceipt> RequestThroughActivity(
        ActivityLedger ledger, ISourceAdapter adapter, RequestId id, WorkKey key, ProcessIncarnation seen, CancellationToken cancellationToken)
    {
        var receipt = ledger.Request(id, ControlAction.StopBackgroundSession, key, seen, CallerScope.Operator);
        if (receipt.Stage == ReceiptStage.Accepted)
        {
            var target = ledger.Find(key)!;
            var control = new VerifiedControl(id, receipt.Action, target.Reference!, ledger.Source(key.Source)!.Generation, seen, target.State);
            await adapter.DispatchAsync(control, cancellationToken);
        }

        return receipt;
    }

    private WorkView Write(string label, LedgerScenario scenario, WorkKey key)
    {
        var view = scenario.View(key);
        output.WriteLine($"t+{(scenario.Now - Start).TotalSeconds}s {label}: {view.Key} {view.State}/{view.Reason} incarnations={view.Incarnations.Length}");
        return view;
    }

    private void Write(string label, ControlReceipt receipt) =>
        output.WriteLine($"{label}: {string.Join(" -> ", receipt.History.Select(step => step.Stage))} refusal={receipt.Refusal?.ToString() ?? "none"} dispatched={receipt.WorkbenchDispatched}");

    private sealed class CountingAdapter(SourceGeneration generation, TimeProvider clock) : ISourceAdapter
    {
        public static readonly WorkKey Key = Session(SessionA);
        public static readonly ProcessIncarnation Live = Process(101);

        public int Dispatches { get; private set; }

        public SourceKind Kind => SourceKind.Claude;

        public Task<CapabilityReport> DescribeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CapabilityReportTests.ClaudeReport(generation, "2.1.312"));

        public async IAsyncEnumerable<SafeSourceBatch> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.True(ControlReference.TrySourceStable(Key, ShortA, out var reference));
            await Task.Yield();
            yield return SafeSourceBatch.Polled(
                Claude,
                generation,
                clock.GetUtcNow(),
                PollEvery,
                [ListedWork.Entry(Key, Entrypoint.Background, ListedStatus.Idle, incarnation: Live, ownership: OwnershipEvidence.SameUserBackgroundListing, reference: reference)],
                [Live],
                processInventoryComplete: true);
        }

        public Task<ControlDispatch> DispatchAsync(VerifiedControl control, CancellationToken cancellationToken)
        {
            this.Dispatches++;
            return Task.FromResult(ControlDispatch.Acknowledged(clock.GetUtcNow()));
        }
    }
}

namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class LivenessReducerTests(ITestOutputHelper output)
{
    private static readonly Signal[] Unlisted =
        [Signal.UnlistedAlive, Signal.UnlistedExited, Signal.UnlistedNoProcess, Signal.UnlistedLivenessUnknown, Signal.UnlistedAfterStop];

    [Fact]
    public void TablePrintsOneLinePerAllowedTransitionAndDecidesEveryStateAndSignal()
    {
        foreach (var transition in Liveness.Transitions)
        {
            output.WriteLine(transition.ToString());
        }

        Assert.NotEmpty(Liveness.Transitions);
        Assert.Equal(Liveness.Transitions.Length, Liveness.Transitions.DistinctBy(t => (t.From, t.Signal)).Count());
        foreach (var from in Liveness.Domain)
        {
            foreach (var signal in Enum.GetValues<Signal>())
            {
                Assert.True(Liveness.IsDecided(from, signal), $"{signal} from {from?.ToString() ?? "new work"} has no rule.");
            }
        }
    }

    [Fact]
    public void NoTransitionEntersAStateFromWhatTheContractSaysNeverEntersIt()
    {
        var moves = Liveness.Transitions;

        Assert.DoesNotContain(moves, t => t.To == WorkState.Starting && t.From is WorkState.Busy or WorkState.Waiting or WorkState.Idle or WorkState.Restarting);
        Assert.All(moves.Where(t => t.To == WorkState.Busy), t => Assert.Equal(Signal.ListedBusy, t.Signal));
        Assert.All(moves.Where(t => t.To == WorkState.Idle), t => Assert.Equal(Signal.ListedIdle, t.Signal));
        Assert.All(moves.Where(t => t.To == WorkState.Waiting), t => Assert.Contains(t.Signal, new[] { Signal.ListedWaiting, Signal.HookWaiting }));
        Assert.All(moves.Where(t => t.To == WorkState.Stopped), t => Assert.Equal(Signal.ListedStopped, t.Signal));
        Assert.All(moves.Where(t => t.To == WorkState.NotLoaded), t => Assert.Equal(Signal.ListedNotLoaded, t.Signal));
        Assert.DoesNotContain(moves, t => t.To == WorkState.Restarting && Unlisted.Contains(t.Signal));
        Assert.All(
            moves.Where(t => t.To == WorkState.Ended),
            t => Assert.Contains(t.Signal, new[] { Signal.UnlistedExited, Signal.SessionEndThenExit, Signal.UnlistedAfterStop }));
        Assert.All(moves.Where(t => t.Signal == Signal.HookWaiting), t => Assert.Equal(WorkState.Waiting, t.To));
        Assert.DoesNotContain(moves, t => t.From == WorkState.Ended && t.Signal is Signal.SourceDisconnected or Signal.ConfirmationExpired);
    }

    [Fact]
    public void LV2_AKillGapIsRestartingAndARawBlockedStateNeverKeepsWorkLive()
    {
        var scenario = new LedgerScenario(Claude);
        var bare = Session(SessionA);
        var staleStatus = Session(SessionB);
        var first = Process(101);
        var second = Process(102);
        scenario.Poll(
            [
                ListedWork.Entry(bare, Entrypoint.Background, ListedStatus.Idle, SourceLifecycle.Working, first),
                ListedWork.Entry(staleStatus, Entrypoint.Background, ListedStatus.Idle, SourceLifecycle.Working, second),
            ],
            [first, second]);

        scenario.Poll(
            [
                ListedWork.Entry(bare, Entrypoint.Background, lifecycle: SourceLifecycle.Blocked),
                ListedWork.Entry(staleStatus, Entrypoint.Background, ListedStatus.Idle, SourceLifecycle.Blocked),
            ],
            []);

        Assert.Equal((WorkState.Restarting, ReasonCode.ProcessGap), scenario.StateOf(bare));
        Assert.Equal((WorkState.Restarting, ReasonCode.IncarnationExited), scenario.StateOf(staleStatus));
    }

    [Fact]
    public void LV4_AnUnlistedEntryWhoseIncarnationIsAliveIsUnknownNotInAgentView()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live)], [live]);

        scenario.Poll([], [live]);

        Assert.Equal((WorkState.Unknown, ReasonCode.NotInAgentView), scenario.StateOf(key));
    }

    [Fact]
    public void LV5_AnUnlistedEntryWhoseIncarnationExitedIsEndedProcessExited()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var exited = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: exited)], [exited]);

        scenario.Poll([], []);

        Assert.Equal((WorkState.Ended, ReasonCode.ProcessExited), scenario.StateOf(key));
    }

    [Fact]
    public void LV6_SessionEndKeepsThePolledStateUntilTheIncarnationExits()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        var idle = ListedWork.Entry(key, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live);
        scenario.Poll([idle], [live]);

        scenario.Hooks(HookEvent.SessionEnd(key));
        Assert.Equal((WorkState.Idle, ReasonCode.SourceReported), scenario.StateOf(key));
        scenario.Poll([idle], [live]);
        Assert.Equal((WorkState.Idle, ReasonCode.SourceReported), scenario.StateOf(key));
        scenario.Poll([ListedWork.Entry(key, Entrypoint.InteractiveCli)], [live]);
        Assert.Equal((WorkState.Restarting, ReasonCode.ProcessGap), scenario.StateOf(key));

        scenario.Poll([], []);
        Assert.Equal((WorkState.Ended, ReasonCode.SessionEndedThenExited), scenario.StateOf(key));
    }

    [Fact]
    public void ID4_ABatchFromAnOlderGenerationChangesNothing()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        var older = scenario.Generation;
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        scenario.Generation = scenario.Ledger.StartGeneration(Claude);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        var before = scenario.View(key);

        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        var late = scenario.Apply(SafeSourceBatch.Polled(Claude, older, scenario.Now, [], [], processInventoryComplete: true));

        Assert.Equal(ApplyOutcome.IgnoredOlderGeneration, late);
        Assert.Equal(before, scenario.View(key));
        Assert.Equal(WorkState.Busy, before.State);
        Assert.Equal(scenario.Generation, scenario.Ledger.Source(Claude)!.Generation);
    }

    [Fact]
    public void ABatchStampedWithAGenerationThisLedgerNeverIssuedIsRejectedAndChangesNothing()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        var before = scenario.View(key);
        var elsewhere = new ActivityLedger(Settings, Clock());
        elsewhere.StartGeneration(Claude);
        var unissued = elsewhere.StartGeneration(Claude);

        scenario.Clock.Advance(Settings.PollInterval);
        var outcome = scenario.Apply(SafeSourceBatch.Polled(Claude, unissued, scenario.Now, [], [], processInventoryComplete: true));

        Assert.Equal(ApplyOutcome.RejectedUnissuedGeneration, outcome);
        Assert.Equal(before, scenario.View(key));
        Assert.Equal(scenario.Generation, scenario.Ledger.Source(Claude)!.Generation);
    }

    [Fact]
    public void AnIncompleteProcessInventoryNeverEndsWork()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live)], [live]);

        scenario.Poll([], [], inventoryComplete: false);

        Assert.Equal((WorkState.Unknown, ReasonCode.ProcessInventoryIncomplete), scenario.StateOf(key));
    }

    [Fact]
    public void StalenessIsDerivedAtReadTimeFromTheInjectedClock()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        var confirmedAt = scenario.Now;

        scenario.Clock.Advance(Settings.StaleAfter);
        Assert.Equal((WorkState.Idle, ReasonCode.SourceReported), scenario.StateOf(key));
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);

        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        var stale = scenario.View(key);
        var source = scenario.Ledger.Source(Claude)!;
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected), (stale.State, stale.Reason));
        Assert.Equal(confirmedAt + Settings.StaleAfter, stale.StateSince);
        Assert.Equal((SourceState.Disconnected, DisconnectReason.Timeout), (source.State, source.Disconnect));
    }

    [Fact]
    public void AnItemThePollingSourceStopsListingGoesStaleForLackOfConfirmation()
    {
        var scenario = new LedgerScenario(Claude);
        var listed = Session(SessionA);
        var dropped = Session(SessionB);
        var live = Process(101);
        var orphan = Process(102);
        var keep = ListedWork.Entry(listed, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live);
        scenario.Poll([keep, ListedWork.Entry(dropped, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: orphan)], [live, orphan]);
        var lastListedAt = scenario.Now;

        while (scenario.Now - lastListedAt <= Settings.StaleAfter)
        {
            scenario.Poll([keep], [live, orphan]);
        }

        var stale = scenario.View(dropped);
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Idle, ReasonCode.SourceReported), scenario.StateOf(listed));
        Assert.Equal((WorkState.Stale, ReasonCode.NoConfirmation), (stale.State, stale.Reason));
        Assert.Equal(lastListedAt + Settings.StaleAfter, stale.StateSince);
    }

    [Fact]
    public void HooksDuringPollSilenceConfirmNothingAndTheWorkGoesStaleWithItsSilentSource()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        var polledAt = scenario.Now;

        while (scenario.Now - polledAt <= Settings.StaleAfter)
        {
            scenario.Hooks(HookEvent.Notification(key, WaitingFor.PermissionPrompt));
        }

        var view = scenario.View(key);
        Assert.Equal(SourceState.Disconnected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected), (view.State, view.Reason));
        Assert.Equal(polledAt, view.LastConfirmedAt);
    }

    [Fact]
    public void ID2_ANewIncarnationBetweenSamplesKeepsTheKeyAndInventsNoRestarting()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var first = Process(101);
        var replacement = Process(102, startedMinutesBefore: 0);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: first)], [first]);
        var idleSince = scenario.View(key).StateSince;

        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: replacement)], [replacement]);

        var view = scenario.View(key);
        Assert.Equal(WorkState.Idle, view.State);
        Assert.Equal(idleSince, view.StateSince);
        Assert.Equal(new[] { first, replacement }, view.Incarnations.Select(association => association.Incarnation));
        Assert.Single(scenario.Ledger.Work(Claude));
    }

    [Fact]
    public void ApplyingTheSameBatchTwiceChangesNothingTheSecondTime()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Clock.Advance(Settings.PollInterval);
        var batch = SafeSourceBatch.Polled(
            Claude, scenario.Generation, scenario.Now, [ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live], true);
        scenario.Apply(batch);
        var once = scenario.View(key);

        Assert.Equal(ApplyOutcome.Applied, scenario.Apply(batch));

        Assert.Equal(once, scenario.View(key));
    }

    [Fact]
    public void ABareEntryPastTheStartingWindowReadsUnknownStartupOverdue()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var bare = ListedWork.Entry(key, Entrypoint.Background);
        scenario.Poll([bare], []);
        var firstSeen = scenario.Now;
        Assert.Equal((WorkState.Starting, ReasonCode.FirstSeen), scenario.StateOf(key));

        while (scenario.Now - firstSeen <= Settings.StartingWindow)
        {
            scenario.Poll([bare], []);
        }

        Assert.Equal((WorkState.Unknown, ReasonCode.StartupOverdue), scenario.StateOf(key));
    }

    [Fact]
    public void ARestartPastTheRestartWindowReadsUnknownRestartOverdueAndStaysThere()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        var bare = ListedWork.Entry(key, Entrypoint.Background);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        scenario.Poll([bare], []);
        var gapStart = scenario.Now;
        Assert.Equal((WorkState.Restarting, ReasonCode.ProcessGap), scenario.StateOf(key));

        while (scenario.Now - gapStart <= Settings.RestartWindow)
        {
            scenario.Poll([bare], []);
        }

        Assert.Equal((WorkState.Unknown, ReasonCode.RestartOverdue), scenario.StateOf(key));
        scenario.Poll([bare], []);
        Assert.Equal((WorkState.Unknown, ReasonCode.RestartOverdue), scenario.StateOf(key));
    }

    [Fact]
    public void AnUnavailableSourceStalesItsWorkAndEndsNothing()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        scenario.Clock.Advance(Settings.PollInterval);

        scenario.Apply(SafeSourceBatch.Unavailable(Claude, scenario.Generation, scenario.Now));

        Assert.Equal((WorkState.Stale, ReasonCode.SourceUnavailable), scenario.StateOf(key));
        Assert.Equal(SourceState.Unavailable, scenario.Ledger.Source(Claude)!.State);
    }

    [Fact]
    public void AStoppedSessionRemovedFromTheListingReadsEnded()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, lifecycle: SourceLifecycle.Stopped)], []);
        Assert.Equal((WorkState.Stopped, ReasonCode.SourceReported), scenario.StateOf(key));

        scenario.Poll([], []);

        Assert.Equal((WorkState.Ended, ReasonCode.RemovedAfterStop), scenario.StateOf(key));
    }

    [Fact]
    public void AStoppedLifecycleReadsStoppedOnlyOnABackgroundEntry()
    {
        var scenario = new LedgerScenario(Claude);
        var keys = Enum.GetValues<Entrypoint>().ToDictionary(entrypoint => entrypoint, entrypoint => Session($"stopped-{entrypoint}"));

        scenario.Poll([.. keys.Select(pair => ListedWork.Entry(pair.Value, pair.Key, lifecycle: SourceLifecycle.Stopped))], []);

        Assert.Equal((WorkState.Stopped, ReasonCode.SourceReported), scenario.StateOf(keys[Entrypoint.Background]));
        Assert.All(
            keys.Where(pair => pair.Key != Entrypoint.Background),
            pair => Assert.Equal((WorkState.Unknown, ReasonCode.Contradictory), scenario.StateOf(pair.Value)));
    }

    [Fact]
    public void AStoppedEntryWithNoKnownProcessEndsOnRemovalOnlyUnderACompleteInventory()
    {
        ReasonCode RemovedUnder(bool inventoryComplete, out WorkState state)
        {
            var scenario = new LedgerScenario(Claude);
            var key = Session(SessionA);
            scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, lifecycle: SourceLifecycle.Stopped)], []);
            Assert.Equal((WorkState.Stopped, ReasonCode.SourceReported), scenario.StateOf(key));

            scenario.Poll([], [], inventoryComplete);

            (state, var reason) = scenario.StateOf(key);
            return reason;
        }

        Assert.Equal(ReasonCode.ProcessInventoryIncomplete, RemovedUnder(inventoryComplete: false, out var partial));
        Assert.Equal(WorkState.Unknown, partial);
        Assert.Equal(ReasonCode.RemovedAfterStop, RemovedUnder(inventoryComplete: true, out var complete));
        Assert.Equal(WorkState.Ended, complete);
    }

    [Fact]
    public void HooksNeverCreateWorkAndOnlyKnownNotificationTypesProjectWaiting()
    {
        var scenario = new LedgerScenario(Claude);
        var busy = Session(SessionA);
        var unknownType = Session(SessionB);
        var live = Process(101);
        var other = Process(102);
        scenario.Poll(
            [
                ListedWork.Entry(busy, Entrypoint.Background, ListedStatus.Busy, incarnation: live),
                ListedWork.Entry(unknownType, Entrypoint.Background, ListedStatus.Busy, incarnation: other),
            ],
            [live, other]);

        scenario.Hooks(
            HookEvent.Notification(busy, WaitingFor.PermissionPrompt),
            HookEvent.Notification(unknownType, WaitingFor.Unknown),
            HookEvent.Notification(Session(SessionC), WaitingFor.PermissionPrompt));

        Assert.Equal((WorkState.Waiting, ReasonCode.NotificationHook), scenario.StateOf(busy));
        Assert.Equal(Channel.Hook, scenario.View(busy).SetBy);
        Assert.Equal((WorkState.Busy, ReasonCode.SourceReported), scenario.StateOf(unknownType));
        Assert.Equal(WaitingFor.Unknown, scenario.View(unknownType).WaitingFor);
        Assert.Null(scenario.Ledger.Find(Session(SessionC)));
    }
}

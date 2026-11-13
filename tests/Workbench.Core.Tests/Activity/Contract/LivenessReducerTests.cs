namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class LivenessReducerTests(ITestOutputHelper output)
{
    private static readonly TimeSpan FiveSeconds = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SixtySeconds = TimeSpan.FromSeconds(60);

    private static readonly Signal[] Unlisted =
        [Signal.UnlistedAlive, Signal.UnlistedExited, Signal.UnlistedNoProcess, Signal.UnlistedLivenessUnknown, Signal.UnlistedAfterStop, Signal.UnlistedSettled];

    public enum Interruption
    {
        PartialInventory,
        FailedAttempt,
        Unavailable,
        LatePoll,
        Relisted,
        NewGeneration,
    }

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
        Assert.All(moves.Where(t => t.To == WorkState.Lost), t => Assert.Equal(Signal.UnlistedSettled, t.Signal));
        Assert.DoesNotContain(moves, t => t.To == WorkState.Lost && t.From is null or WorkState.Stopped or WorkState.Ended);
        Assert.DoesNotContain(moves, t => t.From is WorkState.Ended or WorkState.Lost && t.Signal is Signal.SourceDisconnected or Signal.ConfirmationExpired);
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
        var late = scenario.Apply(SafeSourceBatch.Polled(Claude, older, scenario.Now, PollEvery, [], [], processInventoryComplete: true));

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

        scenario.Clock.Advance(PollEvery);
        var outcome = scenario.Apply(SafeSourceBatch.Polled(Claude, unissued, scenario.Now, PollEvery, [], [], processInventoryComplete: true));

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
    public void LV8_UnlistedWorkWithNoMatchingProcessIsLostOnlyOnceTheWholeSettlingWindowPasses()
    {
        var scenario = new LedgerScenario(Claude);
        var vanished = Session(SessionA);
        var stillRunning = Session(SessionB);
        var process = Process(102);
        scenario.Poll(
            [ListedWork.Entry(vanished, Entrypoint.Background), ListedWork.Entry(stillRunning, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: process)],
            [process]);
        scenario.Poll([], [process]);
        var unlistedAt = scenario.Now;

        while (scenario.Now - unlistedAt <= Settings.LostSettlingWindow)
        {
            Assert.DoesNotContain(scenario.View(vanished).State, new[] { WorkState.Lost, WorkState.Ended });
            scenario.Poll([], [process]);
        }

        var lost = scenario.View(vanished);
        Assert.Equal((WorkState.Lost, ReasonCode.AbsentThroughSettling, scenario.Now), (lost.State, lost.Reason, lost.StateSince));
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Stale, ReasonCode.NoConfirmation), scenario.StateOf(stillRunning));
    }

    [Theory]
    [InlineData(Interruption.PartialInventory)]
    [InlineData(Interruption.FailedAttempt)]
    [InlineData(Interruption.Unavailable)]
    [InlineData(Interruption.LatePoll)]
    [InlineData(Interruption.Relisted)]
    [InlineData(Interruption.NewGeneration)]
    public void LV8_AnInterruptionInsideTheSettlingWindowRestartsIt(Interruption interruption)
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var bare = ListedWork.Entry(key, Entrypoint.Background);
        scenario.Poll([bare], []);
        scenario.Poll([], []);
        var unlistedAt = scenario.Now;
        while (scenario.Now - unlistedAt < Settings.LostSettlingWindow / 2)
        {
            scenario.Poll([], []);
        }

        switch (interruption)
        {
            case Interruption.PartialInventory:
                scenario.Poll([], [], inventoryComplete: false);
                break;
            case Interruption.FailedAttempt:
                scenario.Fail(DisconnectReason.Timeout);
                break;
            case Interruption.Unavailable:
                scenario.Unavailable();
                break;
            case Interruption.LatePoll:
                scenario.Clock.Advance(PollEvery + Settings.MissedPollMargin + TimeSpan.FromTicks(1));
                break;
            case Interruption.Relisted:
                scenario.Poll([bare], []);
                break;
            case Interruption.NewGeneration:
                scenario.Generation = scenario.Ledger.StartGeneration(Claude);
                break;
        }

        scenario.Poll([], []);
        var resumedAt = scenario.Now;
        while (scenario.Now - resumedAt <= Settings.LostSettlingWindow)
        {
            Assert.NotEqual(WorkState.Lost, scenario.View(key).State);
            scenario.Poll([], []);
        }

        Assert.True(resumedAt - unlistedAt >= Settings.LostSettlingWindow / 2);
        Assert.Equal((WorkState.Lost, ReasonCode.AbsentThroughSettling), scenario.StateOf(key));
    }

    [Fact]
    public void LV8_LostWorkListedAgainStartsANewLifeUnderTheSameKey()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        Assert.True(ControlReference.TrySourceStable(key, ShortA, out var reference));
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ownership: OwnershipEvidence.SameUserBackgroundListing, reference: reference)], []);
        var firstSeenAt = scenario.Now;
        PollUntilLost(scenario, key);
        Assert.True(scenario.Now - firstSeenAt > Settings.StartingWindow);

        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background)], []);

        Assert.Equal((WorkState.Starting, ReasonCode.FirstSeen), scenario.StateOf(key));
        Assert.Equal(OwnershipEvidence.None, scenario.View(key).Ownership);
        Assert.Single(scenario.Ledger.Work(Claude));
    }

    [Fact]
    public void LV8_LostIsTerminalSoFailuresUnavailabilityPartialInventoriesAndSilenceLeaveItAsItIs()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background)], []);
        PollUntilLost(scenario, key);
        var lost = scenario.View(key);

        FailBelowTheLimit(scenario);
        scenario.Fail(DisconnectReason.Timeout);
        Assert.Equal(SourceState.Disconnected, scenario.Ledger.Source(Claude)!.State);
        scenario.Unavailable();
        scenario.Poll([], [], inventoryComplete: false);
        scenario.Clock.Advance(UntilMissedPollLimit(PollEvery) * 2);

        Assert.Equal(lost, scenario.View(key));
        Assert.Equal(WorkState.Lost, lost.State);
    }

    [Fact]
    public void LV9_AtAFiveSecondIntervalTheSourceAndItsWorkGoStaleOnlyOnceTheMissedPollLimitPasses()
    {
        var scenario = new LedgerScenario(Claude) { Interval = FiveSeconds };
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        var polledAt = scenario.Now;
        var limitPassesAt = polledAt + (FiveSeconds * Settings.MissedPollLimit) + Settings.MissedPollMargin;

        scenario.Clock.Advance((FiveSeconds * (Settings.MissedPollLimit - 1)) + Settings.MissedPollMargin + TimeSpan.FromSeconds(1));
        AssertFresh(scenario, key, WorkState.Idle);
        scenario.Clock.Advance(limitPassesAt - scenario.Now);
        AssertFresh(scenario, key, WorkState.Idle);

        scenario.Clock.Advance(TimeSpan.FromTicks(1));
        var stale = scenario.View(key);
        var source = scenario.Ledger.Source(Claude)!;
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected), (stale.State, stale.Reason));
        Assert.Equal(limitPassesAt, stale.StateSince);
        Assert.Equal(polledAt, stale.LastConfirmedAt);
        Assert.Equal((SourceState.Disconnected, DisconnectReason.Timeout), (source.State, source.Disconnect));
    }

    [Fact]
    public void LV9_FailedAttemptsBelowTheLimitKeepTheSourceConnectedAndTheLimitDisconnectsItWithTheLastFailuresReason()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        var busy = ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live);
        scenario.Poll([busy], [live]);
        FailBelowTheLimit(scenario);
        scenario.Poll([busy], [live]);
        FailBelowTheLimit(scenario);
        AssertFresh(scenario, key, WorkState.Busy);

        scenario.Fail(DisconnectReason.OutputTooLarge);

        var source = scenario.Ledger.Source(Claude)!;
        var view = scenario.View(key);
        Assert.Equal((SourceState.Disconnected, DisconnectReason.OutputTooLarge), (source.State, source.Disconnect));
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected), (view.State, view.Reason));
        Assert.Equal(scenario.Now, view.StateSince);
    }

    [Fact]
    public void LV9_SilenceAfterAFailedAttemptDisconnectsTheSourceWithThatAttemptsReason()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        scenario.Fail(DisconnectReason.TransportError);
        var limitPassesAt = scenario.Now + (PollEvery * (Settings.MissedPollLimit - 1)) + Settings.MissedPollMargin;

        scenario.Clock.Advance(limitPassesAt - scenario.Now);
        AssertFresh(scenario, key, WorkState.Busy);
        scenario.Clock.Advance(TimeSpan.FromTicks(1));

        var source = scenario.Ledger.Source(Claude)!;
        var view = scenario.View(key);
        Assert.Equal((SourceState.Disconnected, DisconnectReason.TransportError), (source.State, source.Disconnect));
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected, limitPassesAt), (view.State, view.Reason, view.StateSince));
    }

    [Fact]
    public void LV9_AFailedAttemptAfterSilencePassedTheLimitKeepsTheSourceDisconnectedAndItsWorkStale()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        var limitPassesAt = scenario.Now + UntilMissedPollLimit(PollEvery);
        scenario.Clock.Advance(TimeSpan.FromSeconds(20));

        scenario.Fail(DisconnectReason.NonZeroExit);

        var source = scenario.Ledger.Source(Claude)!;
        var view = scenario.View(key);
        Assert.Equal((SourceState.Disconnected, DisconnectReason.NonZeroExit), (source.State, source.Disconnect));
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected, limitPassesAt), (view.State, view.Reason, view.StateSince));
    }

    [Fact]
    public void LV9_AFailedAttemptAfterSilenceAddsItselfToTheExpectedPollsMissedBeforeIt()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        var firstFailureAt = scenario.Now + TimeSpan.FromSeconds(6.4);
        Assert.True(firstFailureAt < scenario.Now + UntilMissedPollLimit(PollEvery));

        foreach (var gap in new[] { 6.4, 4.4, 4.4 })
        {
            scenario.Clock.Advance(TimeSpan.FromSeconds(gap));
            scenario.Fail(DisconnectReason.Timeout);

            var view = scenario.View(key);
            Assert.Equal(SourceState.Disconnected, scenario.Ledger.Source(Claude)!.State);
            Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected, firstFailureAt), (view.State, view.Reason, view.StateSince));
        }
    }

    [Fact]
    public void LV9_ALateFailedAttemptFillsTheExpectedPollItOverranSoSilenceAfterItDisconnectsWhenSilenceAloneWould()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        var limitPassesAt = scenario.Now + UntilMissedPollLimit(PollEvery);
        scenario.Clock.Advance(PollEvery + Settings.MissedPollMargin + TimeSpan.FromMilliseconds(100));
        scenario.Fail(DisconnectReason.TransportError);

        scenario.Clock.Advance(limitPassesAt - scenario.Now);
        AssertFresh(scenario, key, WorkState.Busy);
        scenario.Clock.Advance(TimeSpan.FromTicks(1));

        var source = scenario.Ledger.Source(Claude)!;
        var view = scenario.View(key);
        Assert.Equal((SourceState.Disconnected, DisconnectReason.TransportError), (source.State, source.Disconnect));
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected, limitPassesAt), (view.State, view.Reason, view.StateSince));
    }

    [Theory]
    [InlineData(-1, SourceState.Connected)]
    [InlineData(0, SourceState.Disconnected)]
    public void LV9_AFailedAttemptFillsTheLatestExpectedPollAlreadyDueAndEachEarlierOneIsAMiss(long ticksFromTheLimitthExpectedPoll, SourceState expected)
    {
        var scenario = new LedgerScenario(Claude);
        scenario.Poll([], []);
        scenario.Clock.Advance((PollEvery * Settings.MissedPollLimit) + TimeSpan.FromTicks(ticksFromTheLimitthExpectedPoll));

        scenario.Fail(DisconnectReason.Timeout);

        Assert.Equal(expected, scenario.Ledger.Source(Claude)!.State);
    }

    [Fact]
    public void LV9_AFailedAttemptIsOneMoreMissForAnItemThatPollsStoppedListing()
    {
        var scenario = new LedgerScenario(Claude);
        var listed = Session(SessionA);
        var dropped = Session(SessionB);
        var live = Process(101);
        var orphan = Process(102);
        var keep = ListedWork.Entry(listed, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live);
        scenario.Poll([keep, ListedWork.Entry(dropped, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: orphan)], [live, orphan]);
        for (var unlisted = 1; unlisted < Settings.MissedPollLimit; unlisted++)
        {
            scenario.Poll([keep], [live, orphan]);
        }

        Assert.Equal((WorkState.Unknown, ReasonCode.NotInAgentView), scenario.StateOf(dropped));

        scenario.Fail(DisconnectReason.Timeout);

        Assert.Equal((WorkState.Stale, ReasonCode.NoConfirmation), scenario.StateOf(dropped));
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
    }

    [Fact]
    public void LV9_AnUnavailableAttemptIsOneMoreMissForAnItemThatPollsStoppedListing()
    {
        var scenario = new LedgerScenario(Claude);
        var listed = Session(SessionA);
        var dropped = Session(SessionB);
        var live = Process(101);
        var orphan = Process(102);
        var keep = ListedWork.Entry(listed, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live);
        scenario.Poll([keep, ListedWork.Entry(dropped, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: orphan)], [live, orphan]);
        for (var unlisted = 2; unlisted < Settings.MissedPollLimit; unlisted++)
        {
            scenario.Poll([keep], [live, orphan]);
        }

        scenario.Unavailable();
        scenario.Poll([keep], [live, orphan]);

        Assert.Equal((WorkState.Stale, ReasonCode.NoConfirmation), scenario.StateOf(dropped));
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
    }

    [Fact]
    public void LV9_ALatePollCountsTheExpectedPollsMissedBeforeItAgainstAnItemItDoesNotList()
    {
        var scenario = new LedgerScenario(Claude);
        var listed = Session(SessionA);
        var dropped = Session(SessionB);
        var live = Process(101);
        var orphan = Process(102);
        var keep = ListedWork.Entry(listed, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live);
        scenario.Poll([keep, ListedWork.Entry(dropped, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: orphan)], [live, orphan]);
        scenario.Poll([keep], [live, orphan]);
        var unlistedAt = scenario.Now;
        Assert.Equal((WorkState.Unknown, ReasonCode.NotInAgentView), scenario.StateOf(dropped));

        scenario.Clock.Advance((PollEvery * 2) + Settings.MissedPollMargin + TimeSpan.FromMilliseconds(100));
        scenario.Poll([keep], [live, orphan]);

        var stale = scenario.View(dropped);
        Assert.Equal((WorkState.Stale, ReasonCode.NoConfirmation, unlistedAt + (PollEvery * 2) + Settings.MissedPollMargin), (stale.State, stale.Reason, stale.StateSince));
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Idle, ReasonCode.SourceReported), scenario.StateOf(listed));
    }

    [Fact]
    public void LV9_PollsThatEachOverrunTheirExpectedPollStaleAnItemTheyDoNotListAtTheLimitthPoll()
    {
        var scenario = new LedgerScenario(Claude);
        var listed = Session(SessionA);
        var dropped = Session(SessionB);
        var live = Process(101);
        var orphan = Process(102);
        var keep = ListedWork.Entry(listed, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live);
        scenario.Poll([keep, ListedWork.Entry(dropped, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: orphan)], [live, orphan]);
        var lastListedAt = scenario.Now;
        var late = PollEvery + Settings.MissedPollMargin + TimeSpan.FromMilliseconds(100);
        for (var unlisted = 1; unlisted < Settings.MissedPollLimit; unlisted++)
        {
            scenario.Clock.Advance(late);
            scenario.Poll([keep], [live, orphan]);
            Assert.Equal((WorkState.Unknown, ReasonCode.NotInAgentView), scenario.StateOf(dropped));
        }

        scenario.Clock.Advance(late);
        scenario.Poll([keep], [live, orphan]);

        var stale = scenario.View(dropped);
        Assert.Equal((WorkState.Stale, ReasonCode.NoConfirmation, lastListedAt + UntilMissedPollLimit(PollEvery)), (stale.State, stale.Reason, stale.StateSince));
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Idle, ReasonCode.SourceReported), scenario.StateOf(listed));
    }

    [Fact]
    public void ASourceWhoseEveryAttemptFailedReadsDisconnectedWithTheLastReason()
    {
        var scenario = new LedgerScenario(Claude);
        Assert.Null(scenario.Ledger.Source(Claude));

        scenario.Fail(DisconnectReason.NonZeroExit);

        var source = scenario.Ledger.Source(Claude)!;
        Assert.Equal((SourceState.Disconnected, DisconnectReason.NonZeroExit), (source.State, source.Disconnect));
        Assert.Null(source.LastSucceededAt);
    }

    [Fact]
    public void AnUnavailableSourceThatFallsSilentReadsDisconnectedAfterTheMissedPollLimit()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live]);
        scenario.Unavailable();
        var reportedAt = scenario.Now;

        scenario.Clock.Advance(UntilMissedPollLimit(PollEvery));
        Assert.Equal(SourceState.Unavailable, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Stale, ReasonCode.SourceUnavailable), scenario.StateOf(key));
        scenario.Clock.Advance(TimeSpan.FromTicks(1));

        var source = scenario.Ledger.Source(Claude)!;
        var view = scenario.View(key);
        Assert.Equal((SourceState.Disconnected, DisconnectReason.Timeout), (source.State, source.Disconnect));
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected, reportedAt + UntilMissedPollLimit(PollEvery)), (view.State, view.Reason, view.StateSince));
    }

    [Fact]
    public void LV9_SwitchingToASixtySecondIntervalKeepsWorkFreshBetweenOnTimePolls()
    {
        var scenario = new LedgerScenario(Claude) { Interval = FiveSeconds };
        var key = Session(SessionA);
        var live = Process(101);
        var idle = ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live);
        scenario.Poll([idle], [live]);
        scenario.Poll([idle], [live]);
        Assert.True(TimeSpan.FromSeconds(59) > UntilMissedPollLimit(FiveSeconds));

        scenario.Interval = SixtySeconds;
        scenario.Poll([idle], [live]);
        scenario.Clock.Advance(TimeSpan.FromSeconds(59));
        AssertFresh(scenario, key, WorkState.Idle);

        scenario.Poll([idle], [live]);
        scenario.Clock.Advance(TimeSpan.FromSeconds(59));
        AssertFresh(scenario, key, WorkState.Idle);
    }

    [Fact]
    public void LV9_SwitchingBackToAFiveSecondIntervalStalesWorkAfterTheLimitOfFiveSecondPolls()
    {
        var scenario = new LedgerScenario(Claude) { Interval = SixtySeconds };
        var key = Session(SessionA);
        var live = Process(101);
        var busy = ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live);
        scenario.Poll([busy], [live]);
        scenario.Interval = FiveSeconds;
        scenario.Poll([busy], [live]);
        var polledAt = scenario.Now;

        scenario.Clock.Advance(UntilMissedPollLimit(FiveSeconds));
        AssertFresh(scenario, key, WorkState.Busy);
        scenario.Clock.Advance(TimeSpan.FromTicks(1));

        var stale = scenario.View(key);
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected), (stale.State, stale.Reason));
        Assert.Equal(polledAt + UntilMissedPollLimit(FiveSeconds), stale.StateSince);
        Assert.Equal(SourceState.Disconnected, scenario.Ledger.Source(Claude)!.State);
    }

    [Fact]
    public void LV9_AnUnlistedItemGoesStaleAfterTheLimitOfPollsThatDidNotListItNotAfterElapsedTime()
    {
        var scenario = new LedgerScenario(Claude) { Interval = FiveSeconds };
        var listed = Session(SessionA);
        var dropped = Session(SessionB);
        var live = Process(101);
        var orphan = Process(102);
        var keep = ListedWork.Entry(listed, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: live);
        scenario.Poll([keep, ListedWork.Entry(dropped, Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: orphan)], [live, orphan]);
        var lastListedAt = scenario.Now;
        scenario.Poll([keep], [live, orphan]);

        scenario.Interval = SixtySeconds;
        for (var missed = 2; missed < Settings.MissedPollLimit; missed++)
        {
            scenario.Poll([keep], [live, orphan]);
        }

        scenario.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.True(scenario.Now - lastListedAt > UntilMissedPollLimit(FiveSeconds));
        Assert.Equal((WorkState.Unknown, ReasonCode.NotInAgentView), scenario.StateOf(dropped));

        scenario.Poll([keep], [live, orphan]);

        var stale = scenario.View(dropped);
        Assert.Equal((WorkState.Stale, ReasonCode.NoConfirmation), (stale.State, stale.Reason));
        Assert.Equal(scenario.Now, stale.StateSince);
        Assert.Equal(SourceState.Connected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Idle, ReasonCode.SourceReported), scenario.StateOf(listed));
    }

    [Fact]
    public void LV9_HooksDuringPollSilenceConfirmNothingAndTheWorkGoesStaleWithItsSilentSource()
    {
        var scenario = new LedgerScenario(Claude);
        var key = Session(SessionA);
        var live = Process(101);
        scenario.Poll([ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Busy, incarnation: live)], [live]);
        var polledAt = scenario.Now;

        while (scenario.Now <= polledAt + UntilMissedPollLimit(PollEvery))
        {
            scenario.Hooks(HookEvent.Notification(key, WaitingFor.PermissionPrompt));
        }

        var view = scenario.View(key);
        Assert.Equal(SourceState.Disconnected, scenario.Ledger.Source(Claude)!.State);
        Assert.Equal((WorkState.Stale, ReasonCode.SourceDisconnected), (view.State, view.Reason));
        Assert.Equal(polledAt + UntilMissedPollLimit(PollEvery), view.StateSince);
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
        var dropped = Session(SessionB);
        var live = Process(101);
        var orphan = Process(102);
        var keep = ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live);
        scenario.Poll([keep, ListedWork.Entry(dropped, Entrypoint.Background, ListedStatus.Idle, incarnation: orphan)], [live, orphan]);
        for (var missed = 1; missed < Settings.MissedPollLimit - 1; missed++)
        {
            scenario.Poll([keep], [live, orphan]);
        }

        scenario.Clock.Advance(PollEvery);
        var batch = SafeSourceBatch.Polled(Claude, scenario.Generation, scenario.Now, PollEvery, [keep], [live, orphan], true);
        scenario.Apply(batch);
        var once = (scenario.View(key), scenario.View(dropped));
        Assert.Equal(WorkState.Unknown, once.Item2.State);

        Assert.Equal(ApplyOutcome.Applied, scenario.Apply(batch));

        Assert.Equal(once, (scenario.View(key), scenario.View(dropped)));
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

        scenario.Unavailable();

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

    private static void PollUntilLost(LedgerScenario scenario, WorkKey key)
    {
        var deadline = scenario.Now + (Settings.LostSettlingWindow * 2);
        while (scenario.View(key).State != WorkState.Lost)
        {
            Assert.True(scenario.Now < deadline, "The work never settled into Lost.");
            scenario.Poll([], []);
        }
    }

    private static void FailBelowTheLimit(LedgerScenario scenario)
    {
        for (var failed = 1; failed < Settings.MissedPollLimit; failed++)
        {
            scenario.Fail(failed % 2 == 0 ? DisconnectReason.NonZeroExit : DisconnectReason.MalformedOutput);
        }
    }

    private static void AssertFresh(LedgerScenario scenario, WorkKey key, WorkState state)
    {
        Assert.Equal((state, ReasonCode.SourceReported), scenario.StateOf(key));
        Assert.Equal((SourceState.Connected, (DisconnectReason?)null), (scenario.Ledger.Source(scenario.Source)!.State, scenario.Ledger.Source(scenario.Source)!.Disconnect));
    }
}

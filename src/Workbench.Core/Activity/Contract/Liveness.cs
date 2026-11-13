namespace Workbench.Core.Activity.Contract;

using System.Collections.Immutable;

public enum WorkState
{
    Starting,
    Busy,
    Waiting,
    Idle,
    Restarting,
    Stopped,
    Ended,
    Stale,
    Unknown,
    NotLoaded,
}

public enum SourceState
{
    Connected,
    Disconnected,
    Unavailable,
    Unsupported,
}

public enum ReasonCode
{
    FirstSeen,
    SourceReported,
    NotificationHook,
    ProcessGap,
    IncarnationExited,
    ProcessExited,
    SessionEndedThenExited,
    RemovedAfterStop,
    NoConfirmation,
    SourceDisconnected,
    SourceUnavailable,
    SchemaDrift,
    Contradictory,
    NotInAgentView,
    ProcessNotMatched,
    ProcessInventoryIncomplete,
    StartupOverdue,
    RestartOverdue,
    ConnectionLost,
    StoredNotLoaded,
}

internal enum Signal
{
    ListedBusy,
    ListedWaiting,
    ListedIdle,
    ListedStopped,
    ListedNotLoaded,
    ListedUnrecognized,
    ListedContradictory,
    ListedPending,
    ListedPendingOverdue,
    ListedStatusWithoutProcess,
    ListedProcessGap,
    ListedIncarnationExited,
    ListedRestartOverdue,
    SessionEndThenExit,
    UnlistedAlive,
    UnlistedExited,
    UnlistedNoProcess,
    UnlistedLivenessUnknown,
    UnlistedAfterStop,
    HookWaiting,
    SourceDisconnected,
    SourceUnavailable,
    ConnectionLost,
    ConfirmationExpired,
}

public sealed class LivenessSettings
{
    public LivenessSettings(TimeSpan pollInterval, TimeSpan staleAfter, TimeSpan startingWindow, TimeSpan restartWindow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(staleAfter, pollInterval);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(startingWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(restartWindow, TimeSpan.Zero);
        this.PollInterval = pollInterval;
        this.StaleAfter = staleAfter;
        this.StartingWindow = startingWindow;
        this.RestartWindow = restartWindow;
    }

    public TimeSpan PollInterval { get; }

    public TimeSpan StaleAfter { get; }

    public TimeSpan StartingWindow { get; }

    public TimeSpan RestartWindow { get; }
}

internal static class Liveness
{
    private static readonly WorkState?[] Any = [null, .. Enum.GetValues<WorkState>().Select(state => (WorkState?)state)];
    private static readonly WorkState?[] Live = [WorkState.Busy, WorkState.Waiting, WorkState.Idle];
    private static readonly WorkState?[] LiveOrRestarting = [.. Live, WorkState.Restarting];
    private static readonly WorkState?[] Untracked = [null, WorkState.Ended];
    private static readonly WorkState?[] Tracked = Except(Any, Untracked);

    private static readonly ImmutableArray<Row> Rows =
    [
        Move(Any, Signal.ListedBusy, WorkState.Busy, ReasonCode.SourceReported),
        Move(Any, Signal.ListedWaiting, WorkState.Waiting, ReasonCode.SourceReported),
        Move(Any, Signal.ListedIdle, WorkState.Idle, ReasonCode.SourceReported),
        Move(Any, Signal.ListedStopped, WorkState.Stopped, ReasonCode.SourceReported),
        Move(Any, Signal.ListedNotLoaded, WorkState.NotLoaded, ReasonCode.StoredNotLoaded),
        Move(Any, Signal.ListedUnrecognized, WorkState.Unknown, ReasonCode.SchemaDrift),
        Move(Any, Signal.ListedContradictory, WorkState.Unknown, ReasonCode.Contradictory),

        Move(Except(Any, LiveOrRestarting), Signal.ListedPending, WorkState.Starting, ReasonCode.FirstSeen),
        Never(LiveOrRestarting, Signal.ListedPending),
        Move(Except(Any, LiveOrRestarting), Signal.ListedPendingOverdue, WorkState.Unknown, ReasonCode.StartupOverdue),
        Never(LiveOrRestarting, Signal.ListedPendingOverdue),
        Move(Except(Any, LiveOrRestarting), Signal.ListedStatusWithoutProcess, WorkState.Unknown, ReasonCode.ProcessNotMatched),
        Never(LiveOrRestarting, Signal.ListedStatusWithoutProcess),

        Move(Tracked, Signal.ListedProcessGap, WorkState.Restarting, ReasonCode.ProcessGap),
        Never(Untracked, Signal.ListedProcessGap),
        Move(Tracked, Signal.ListedIncarnationExited, WorkState.Restarting, ReasonCode.IncarnationExited),
        Never(Untracked, Signal.ListedIncarnationExited),
        Move([WorkState.Restarting, WorkState.Unknown], Signal.ListedRestartOverdue, WorkState.Unknown, ReasonCode.RestartOverdue),
        Never(Except(Any, [WorkState.Restarting, WorkState.Unknown]), Signal.ListedRestartOverdue),
        Move(Tracked, Signal.SessionEndThenExit, WorkState.Ended, ReasonCode.SessionEndedThenExited),
        Never(Untracked, Signal.SessionEndThenExit),

        Move(Tracked, Signal.UnlistedAlive, WorkState.Unknown, ReasonCode.NotInAgentView),
        Never(Untracked, Signal.UnlistedAlive),
        Move(Except(Tracked, [WorkState.Stopped]), Signal.UnlistedExited, WorkState.Ended, ReasonCode.ProcessExited),
        Never([.. Untracked, WorkState.Stopped], Signal.UnlistedExited),
        Move(Except(Tracked, [WorkState.Stopped]), Signal.UnlistedNoProcess, WorkState.Unknown, ReasonCode.NotInAgentView),
        Never([.. Untracked, WorkState.Stopped], Signal.UnlistedNoProcess),
        Move(Tracked, Signal.UnlistedLivenessUnknown, WorkState.Unknown, ReasonCode.ProcessInventoryIncomplete),
        Never(Untracked, Signal.UnlistedLivenessUnknown),
        Move([WorkState.Stopped], Signal.UnlistedAfterStop, WorkState.Ended, ReasonCode.RemovedAfterStop),
        Never(Except(Any, [WorkState.Stopped]), Signal.UnlistedAfterStop),

        Move(Live, Signal.HookWaiting, WorkState.Waiting, ReasonCode.NotificationHook),
        Hold(Except(Tracked, Live), Signal.HookWaiting),
        Never(Untracked, Signal.HookWaiting),

        Move(Tracked, Signal.SourceDisconnected, WorkState.Stale, ReasonCode.SourceDisconnected),
        Never(Untracked, Signal.SourceDisconnected),
        Move(Tracked, Signal.SourceUnavailable, WorkState.Stale, ReasonCode.SourceUnavailable),
        Never(Untracked, Signal.SourceUnavailable),
        Move(Tracked, Signal.ConnectionLost, WorkState.Unknown, ReasonCode.ConnectionLost),
        Never(Untracked, Signal.ConnectionLost),

        Move(Except(Tracked, [WorkState.Stale]), Signal.ConfirmationExpired, WorkState.Stale, ReasonCode.NoConfirmation),
        Hold([WorkState.Stale, WorkState.Ended], Signal.ConfirmationExpired),
        Never([null], Signal.ConfirmationExpired),
    ];

    private static readonly Cell[,] Cells = Index(Rows);

    internal enum ProcessFate
    {
        Alive,
        Exited,
        Unknown,
    }

    private enum Effect
    {
        Unassigned,
        Move,
        Hold,
        Never,
    }

    internal static IEnumerable<WorkState?> Domain => Any;

    internal static ImmutableArray<Transition> Transitions { get; } =
    [
        .. Rows.Where(row => row.Effect == Effect.Move)
            .SelectMany(row => row.From.Select(from => new Transition(from, row.Signal, row.To, row.Reason))),
    ];

    internal static bool IsDecided(WorkState? from, Signal signal) => CellOf(from, signal).Effect != Effect.Unassigned;

    internal static Transition? Step(WorkState? from, Signal signal)
    {
        var cell = CellOf(from, signal);
        return cell.Effect switch
        {
            Effect.Move => new Transition(from, signal, cell.To, cell.Reason),
            Effect.Hold => null,
            _ => throw new InvalidOperationException($"No liveness rule allows {signal} from {from?.ToString() ?? "new work"}."),
        };
    }

    internal static ProcessFate FateOf(ProcessIncarnation? last, IReadOnlySet<ProcessIncarnation> live, bool inventoryComplete)
    {
        if (last is not { } incarnation)
        {
            return ProcessFate.Unknown;
        }

        if (live.Contains(incarnation))
        {
            return ProcessFate.Alive;
        }

        return inventoryComplete ? ProcessFate.Exited : ProcessFate.Unknown;
    }

    internal static Signal ClassifyListed(in Prior prior, ListedWork entry, ProcessFate lastFate, DateTimeOffset observedAt, LivenessSettings settings)
    {
        if (entry is not ListedWork.RecognizedEntry listed)
        {
            return Signal.ListedUnrecognized;
        }

        if (listed.Lifecycle == SourceLifecycle.Stopped)
        {
            return listed.Incarnation is null && listed.Status == ListedStatus.None ? Signal.ListedStopped : Signal.ListedContradictory;
        }

        if (listed.Status == ListedStatus.NotLoaded)
        {
            return Signal.ListedNotLoaded;
        }

        if (listed.Incarnation is not null)
        {
            return listed.Status switch
            {
                ListedStatus.Busy => Signal.ListedBusy,
                ListedStatus.Waiting => Signal.ListedWaiting,
                ListedStatus.Idle => Signal.ListedIdle,
                _ => Signal.ListedContradictory,
            };
        }

        if (prior.SessionEndSeen && lastFate == ProcessFate.Exited)
        {
            return Signal.SessionEndThenExit;
        }

        if (prior.EverLive)
        {
            return RestartOrOverdue(prior, listed.Status == ListedStatus.None ? Signal.ListedProcessGap : Signal.ListedIncarnationExited, observedAt, settings);
        }

        if (listed.Status != ListedStatus.None)
        {
            return Signal.ListedStatusWithoutProcess;
        }

        return observedAt - prior.FirstSeenAt <= settings.StartingWindow ? Signal.ListedPending : Signal.ListedPendingOverdue;
    }

    internal static Signal ClassifyUnlisted(in Prior prior, ProcessFate lastFate)
    {
        if (prior.SessionEndSeen && lastFate == ProcessFate.Exited)
        {
            return Signal.SessionEndThenExit;
        }

        if (prior.State == WorkState.Stopped)
        {
            return lastFate switch
            {
                ProcessFate.Alive => Signal.UnlistedAlive,
                ProcessFate.Unknown when prior.LastIncarnation is not null => Signal.UnlistedLivenessUnknown,
                _ => Signal.UnlistedAfterStop,
            };
        }

        return lastFate switch
        {
            ProcessFate.Alive => Signal.UnlistedAlive,
            ProcessFate.Exited => Signal.UnlistedExited,
            _ => prior.LastIncarnation is null ? Signal.UnlistedNoProcess : Signal.UnlistedLivenessUnknown,
        };
    }

    internal static Signal? ClassifyHook(WaitingFor waitingFor) =>
        waitingFor is WaitingFor.PermissionPrompt or WaitingFor.IdlePrompt or WaitingFor.AgentNeedsInput or WaitingFor.ElicitationDialog
            ? Signal.HookWaiting
            : null;

    private static Signal RestartOrOverdue(in Prior prior, Signal gap, DateTimeOffset observedAt, LivenessSettings settings) =>
        (prior.State == WorkState.Restarting && observedAt - prior.StateSince > settings.RestartWindow) ||
        (prior.State == WorkState.Unknown && prior.Reason == ReasonCode.RestartOverdue)
            ? Signal.ListedRestartOverdue
            : gap;

    private static Cell CellOf(WorkState? from, Signal signal) => Cells[from is { } state ? (int)state + 1 : 0, (int)signal];

    private static WorkState?[] Except(WorkState?[] states, WorkState?[] excluded) => [.. states.Except(excluded)];

    private static Row Move(WorkState?[] from, Signal signal, WorkState to, ReasonCode reason) => new(from, signal, Effect.Move, to, reason);

    private static Row Hold(WorkState?[] from, Signal signal) => new(from, signal, Effect.Hold, default, default);

    private static Row Never(WorkState?[] from, Signal signal) => new(from, signal, Effect.Never, default, default);

    private static Cell[,] Index(ImmutableArray<Row> rows)
    {
        var cells = new Cell[Any.Length, Enum.GetValues<Signal>().Length];
        foreach (var row in rows)
        {
            foreach (var from in row.From)
            {
                ref var cell = ref cells[from is { } state ? (int)state + 1 : 0, (int)row.Signal];
                if (cell.Effect != Effect.Unassigned)
                {
                    throw new InvalidOperationException($"The liveness table decides {row.Signal} from {from} twice.");
                }

                cell = new Cell(row.Effect, row.To, row.Reason);
            }
        }

        return cells;
    }

    internal readonly record struct Transition(WorkState? From, Signal Signal, WorkState To, ReasonCode Reason)
    {
        public override string ToString() => $"{this.From?.ToString() ?? "(new)"} --{this.Signal}--> {this.To} / {this.Reason}";
    }

    internal readonly record struct Prior(
        WorkState? State,
        ReasonCode Reason,
        DateTimeOffset StateSince,
        DateTimeOffset FirstSeenAt,
        bool EverLive,
        bool SessionEndSeen,
        ProcessIncarnation? LastIncarnation);

    private readonly record struct Cell(Effect Effect, WorkState To, ReasonCode Reason);

    private sealed record Row(WorkState?[] From, Signal Signal, Effect Effect, WorkState To, ReasonCode Reason);
}

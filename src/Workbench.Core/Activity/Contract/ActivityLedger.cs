namespace Workbench.Core.Activity.Contract;

using System.Collections.Immutable;
using System.Diagnostics;

public enum ApplyOutcome
{
    Applied,
    IgnoredOlderGeneration,
    IgnoredOutOfOrder,
    RejectedUnissuedGeneration,
}

public sealed record WorkView
{
    internal WorkView(WorkRecord record, WorkState state, ReasonCode reason, DateTimeOffset stateSince)
    {
        this.Key = record.Key;
        this.State = state;
        this.Reason = reason;
        this.SetBy = record.SetBy;
        this.StateSince = stateSince;
        this.LastConfirmedAt = record.LastConfirmedAt;
        this.SourceReportedAt = record.SourceReportedAt;
        this.Entrypoint = record.Entrypoint;
        this.Project = record.Project;
        this.Parent = record.Parent;
        this.Ownership = record.Ownership;
        this.Lifecycle = record.Lifecycle;
        this.WaitingFor = record.WaitingFor;
        this.Reference = record.Reference;
        this.Incarnations = record.Incarnations;
    }

    public WorkKey Key { get; }

    public WorkState State { get; }

    public ReasonCode Reason { get; }

    public Channel SetBy { get; }

    public DateTimeOffset StateSince { get; }

    public DateTimeOffset LastConfirmedAt { get; }

    public DateTimeOffset? SourceReportedAt { get; }

    public Entrypoint Entrypoint { get; }

    public ProjectRef Project { get; }

    public WorkKey? Parent { get; }

    public OwnershipEvidence Ownership { get; }

    public SourceLifecycle Lifecycle { get; }

    public WaitingFor WaitingFor { get; }

    public ControlReference? Reference { get; }

    public ImmutableArray<Association> Incarnations { get; }
}

public sealed record SourceView
{
    internal SourceView(SourceInstanceId source, SourceGeneration generation, SourceState state, DisconnectReason? disconnect, DateTimeOffset? lastSucceededAt)
    {
        this.Source = source;
        this.Generation = generation;
        this.State = state;
        this.Disconnect = disconnect;
        this.LastSucceededAt = lastSucceededAt;
    }

    public SourceInstanceId Source { get; }

    public SourceGeneration Generation { get; }

    public SourceState State { get; }

    public DisconnectReason? Disconnect { get; }

    public DateTimeOffset? LastSucceededAt { get; }
}

public sealed class ActivityLedger
{
    private static readonly (DisabledReason Disabled, RefusalReason Refusal)[] RefusalPrecedence =
    [
        (DisabledReason.VersionChanged, RefusalReason.VersionChanged),
        (DisabledReason.NoSupportedRoute, RefusalReason.NoSupportedRoute),
        (DisabledReason.NoConditionalDispatch, RefusalReason.NoConditionalDispatch),
        (DisabledReason.NoConformance, RefusalReason.NoConformance),
    ];

    private readonly LivenessSettings settings;
    private readonly TimeProvider clock;
    private readonly Dictionary<SourceInstanceId, SourcePartition> sources = [];
    private readonly Dictionary<RequestId, ControlReceipt> receipts = [];

    public ActivityLedger(LivenessSettings settings, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        this.settings = settings;
        this.clock = clock;
    }

    public SourceGeneration StartGeneration(SourceInstanceId source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var partition = this.PartitionOf(source);
        partition.Generation = new SourceGeneration(checked(partition.Generation.Value + 1));
        partition.LastSnapshotAt = DateTimeOffset.MinValue;
        var now = this.clock.GetUtcNow();
        foreach (var record in partition.Tracked.Where(static record => record.Key.Connection is not null))
        {
            Transit(record, Signal.ConnectionLost, now, Channel.Poll, partition.Generation);
        }

        partition.Tracked.RemoveWhere(static record => record.Key.Connection is not null);
        return partition.Generation;
    }

    public ApplyOutcome Apply(SafeSourceBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.ObservedAt > this.clock.GetUtcNow())
        {
            throw new ArgumentException("The batch is stamped later than the ledger clock.", nameof(batch));
        }

        var partition = this.PartitionOf(batch.Source);
        if (batch.Generation < partition.Generation)
        {
            return ApplyOutcome.IgnoredOlderGeneration;
        }

        if (batch.Generation > partition.Generation)
        {
            return ApplyOutcome.RejectedUnissuedGeneration;
        }

        if (batch is not SafeSourceBatch.HookDelivery && batch.ObservedAt < partition.LastSnapshotAt)
        {
            return ApplyOutcome.IgnoredOutOfOrder;
        }

        switch (batch)
        {
            case SafeSourceBatch.PollSnapshot poll:
                this.ApplyPoll(partition, poll);
                break;
            case SafeSourceBatch.PollFailure failure:
                ApplySourceWide(partition, failure, Signal.SourceDisconnected, SourceState.Disconnected, failure.Reason);
                break;
            case SafeSourceBatch.SourceAbsent absent:
                ApplySourceWide(partition, absent, Signal.SourceUnavailable, SourceState.Unavailable, null);
                break;
            case SafeSourceBatch.HookDelivery delivery:
                ApplyHooks(partition, delivery);
                break;
        }

        return ApplyOutcome.Applied;
    }

    public CapabilityReport Admit(CapabilityReport reported)
    {
        ArgumentNullException.ThrowIfNull(reported);
        var partition = this.PartitionOf(reported.Source);
        if (partition.Admitted is { } admitted && reported.Generation < admitted.Generation)
        {
            return admitted;
        }

        if (reported.SourceVersion is { } version)
        {
            partition.VersionChanged |= partition.LastKnownVersion is { } last && last != version;
            partition.LastKnownVersion = version;
        }

        partition.Admitted = partition.VersionChanged ? reported.WithControlsDisabled(DisabledReason.VersionChanged) : reported;
        return partition.Admitted;
    }

    public ControlReceipt Request(RequestId requestId, ControlAction action, WorkKey target, ProcessIncarnation? seenIncarnation, CallerScope caller)
    {
        if (requestId == default)
        {
            throw Grammar.Invalid(nameof(requestId));
        }

        Grammar.RequireDefined(action, nameof(action));
        Grammar.RequireDefined(caller, nameof(caller));
        ArgumentNullException.ThrowIfNull(target);
        var now = this.clock.GetUtcNow();
        var requested = new ControlReceipt(requestId, action, target, seenIncarnation, caller, now);
        if (this.receipts.TryGetValue(requestId, out var existing))
        {
            if (caller != CallerScope.Operator && caller != existing.Caller)
            {
                return requested.Advance(ReceiptStage.Refused, now, RefusalReason.CallerScope);
            }

            return existing.Action == action && existing.Target == target
                ? existing
                : requested.Advance(ReceiptStage.Refused, now, RefusalReason.RequestIdConflict);
        }

        var receipt = this.Gate(requested, now);
        this.receipts.Add(requestId, receipt);
        return receipt;
    }

    public WorkView? Find(WorkKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return this.sources.TryGetValue(key.Source, out var partition) && partition.Work.TryGetValue(key, out var record)
            ? this.Project(partition, record, this.clock.GetUtcNow())
            : null;
    }

    public IReadOnlyList<WorkView> Work(SourceInstanceId source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var now = this.clock.GetUtcNow();
        return this.sources.TryGetValue(source, out var partition) ? [.. partition.Work.Values.Select(record => this.Project(partition, record, now))] : [];
    }

    public SourceView? Source(SourceInstanceId source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!this.sources.TryGetValue(source, out var partition) || partition.Health is not { } health)
        {
            return null;
        }

        return this.SilentSince(partition, this.clock.GetUtcNow()) is not null
            ? new SourceView(source, partition.Generation, SourceState.Disconnected, DisconnectReason.Timeout, partition.LastSucceededAt)
            : new SourceView(source, partition.Generation, health, partition.Disconnect, partition.LastSucceededAt);
    }

    private static bool RouteFits(ControlAction action, WorkView target) => action switch
    {
        ControlAction.StopBackgroundSession =>
            target.Key.Source.Kind == SourceKind.Claude && target.Key.Kind == WorkKind.Session && target.Entrypoint == Entrypoint.Background,
        ControlAction.TerminateOwnedCommand => target.Key.Source.Kind == SourceKind.Codex && target.Key.Kind == WorkKind.Command,
        ControlAction.InterruptTurn => target.Key.Source.Kind == SourceKind.Codex && target.Key.Kind == WorkKind.Turn,
        _ => false,
    };

    private static bool OwnershipFits(WorkView target) => target.Ownership switch
    {
        OwnershipEvidence.SameUserBackgroundListing =>
            target.Key.Source.Kind == SourceKind.Claude && target.Key.Kind == WorkKind.Session && target.Entrypoint == Entrypoint.Background,
        OwnershipEvidence.WorkbenchOwnedServer =>
            target.Key.Source.Kind == SourceKind.Codex && target.Key.Kind == WorkKind.Command && target.Entrypoint == Entrypoint.Headless,
        _ => false,
    };

    private static RefusalReason RefusalFor(DisabledReason disabled) => RefusalPrecedence.First(entry => disabled.HasFlag(entry.Disabled)).Refusal;

    private static void ApplySourceWide(SourcePartition partition, SafeSourceBatch batch, Signal signal, SourceState health, DisconnectReason? disconnect)
    {
        foreach (var record in partition.Tracked)
        {
            Transit(record, signal, batch.ObservedAt, Channel.Poll, batch.Generation);
        }

        partition.LastSnapshotAt = batch.ObservedAt;
        partition.Health = health;
        partition.Disconnect = disconnect;
    }

    private static void ApplyHooks(SourcePartition partition, SafeSourceBatch.HookDelivery delivery)
    {
        foreach (var hook in delivery.Events)
        {
            if (!partition.Work.TryGetValue(hook.Session, out var record) || !partition.Tracked.Contains(record) || delivery.ObservedAt < record.LastObservedAt)
            {
                continue;
            }

            record.LastObservedAt = delivery.ObservedAt;
            switch (hook)
            {
                case HookEvent.SessionEndEvent:
                    record.SessionEndSeen = true;
                    break;
                case HookEvent.NotificationEvent notification:
                    record.WaitingFor = notification.WaitingFor;
                    if (Liveness.ClassifyHook(notification.WaitingFor) is { } signal)
                    {
                        Transit(record, signal, delivery.ObservedAt, Channel.Hook, delivery.Generation);
                    }

                    break;
            }
        }
    }

    private static void Transit(WorkRecord record, Signal signal, DateTimeOffset at, Channel via, SourceGeneration generation)
    {
        record.LastObservedAt = at;
        if (Liveness.Step(record.State, signal) is not { } step)
        {
            return;
        }

        if (step.To != record.State)
        {
            record.State = step.To;
            record.StateSince = at;
        }

        record.Reason = step.Reason;
        record.SetBy = via;
        record.StateGeneration = generation;
    }

    private void ApplyPoll(SourcePartition partition, SafeSourceBatch.PollSnapshot poll)
    {
        var stamp = ++partition.ListingStamp;
        foreach (var entry in poll.Listing)
        {
            var known = partition.Work.TryGetValue(entry.Work, out var record);
            record ??= new WorkRecord(entry.Work, poll.ObservedAt);
            if (record.State == WorkState.Ended)
            {
                record.StartNewLife(poll.ObservedAt);
            }

            var fate = Liveness.FateOf(record.LastIncarnation, poll.LiveProcesses, poll.ProcessInventoryComplete);
            var signal = Liveness.ClassifyListed(record.Prior, entry, fate, poll.ObservedAt, this.settings);
            record.Confirm(entry, poll.ObservedAt, fate);
            Transit(record, signal, poll.ObservedAt, Channel.Poll, poll.Generation);
            record.ListingStamp = stamp;
            if (!known)
            {
                partition.Work.Add(entry.Work, record);
            }

            partition.Tracked.Add(record);
        }

        foreach (var record in partition.Tracked)
        {
            if (record.ListingStamp != stamp)
            {
                record.LastFate = Liveness.FateOf(record.LastIncarnation, poll.LiveProcesses, poll.ProcessInventoryComplete);
                var signal = Liveness.ClassifyUnlisted(record.Prior, record.LastFate, poll.ProcessInventoryComplete);
                Transit(record, signal, poll.ObservedAt, Channel.Poll, poll.Generation);
            }
        }

        partition.Tracked.RemoveWhere(static record => record.State == WorkState.Ended);
        partition.LastSnapshotAt = poll.ObservedAt;
        partition.LastSucceededAt = poll.ObservedAt;
        partition.Health = SourceState.Connected;
        partition.Disconnect = null;
    }

    private ControlReceipt Gate(ControlReceipt requested, DateTimeOffset now)
    {
        ControlReceipt Refuse(RefusalReason reason) => requested.Advance(ReceiptStage.Refused, now, reason);

        if (requested.Caller != CallerScope.Operator)
        {
            return Refuse(RefusalReason.CallerScope);
        }

        if (!this.sources.TryGetValue(requested.Target.Source, out var partition) || !partition.Work.TryGetValue(requested.Target, out var record))
        {
            return Refuse(RefusalReason.UnknownTarget);
        }

        var view = this.Project(partition, record, now);
        if (!RouteFits(requested.Action, view))
        {
            return Refuse(RefusalReason.NoSupportedRoute);
        }

        if (view.State is WorkState.Stale or WorkState.Unknown)
        {
            return Refuse(RefusalReason.StaleTarget);
        }

        if (!OwnershipFits(view))
        {
            return Refuse(RefusalReason.NoOwnershipEvidence);
        }

        if (requested.Action == ControlAction.StopBackgroundSession && view.State == WorkState.Stopped && record.StateGeneration == partition.Generation &&
            (record.LastIncarnation is null || record.LastFate == Liveness.ProcessFate.Exited))
        {
            return requested.Advance(ReceiptStage.NoActionNeeded, now);
        }

        if (record.Reference is not { } reference || !reference.IsUsableAt(partition.Generation))
        {
            return Refuse(RefusalReason.StaleTarget);
        }

        if (record.LastFate == Liveness.ProcessFate.Alive && requested.SeenIncarnation != record.LastIncarnation)
        {
            return Refuse(RefusalReason.IncarnationChanged);
        }

        var capability = partition.Admitted?.Entrypoints
            .FirstOrDefault(coverage => coverage.Entrypoint == view.Entrypoint)?.Controls
            .FirstOrDefault(control => control.Action == requested.Action && control.Target == requested.Target.Kind);
        return Refuse(capability is null ? RefusalReason.NoSupportedRoute : RefusalFor(capability.Disabled));
    }

    private WorkView Project(SourcePartition partition, WorkRecord record, DateTimeOffset now)
    {
        var stored = record.State ?? throw new UnreachableException();
        var (expiredAt, signal) = this.SilentSince(partition, now) is { } silentSince
            ? (silentSince, Signal.SourceDisconnected)
            : (this.ExpiredAt(record.LastConfirmedAt, now), Signal.ConfirmationExpired);
        return expiredAt is { } since && Liveness.Step(stored, signal) is { } stale
            ? new WorkView(record, stale.To, stale.Reason, since)
            : new WorkView(record, stored, record.Reason, record.StateSince);
    }

    private DateTimeOffset? SilentSince(SourcePartition partition, DateTimeOffset now) =>
        partition.Health == SourceState.Connected ? this.ExpiredAt(partition.LastSucceededAt, now) : null;

    private DateTimeOffset? ExpiredAt(DateTimeOffset? confirmedAt, DateTimeOffset now) =>
        confirmedAt + this.settings.StaleAfter is { } expiry && now > expiry ? expiry : null;

    private SourcePartition PartitionOf(SourceInstanceId source)
    {
        if (!this.sources.TryGetValue(source, out var partition))
        {
            partition = new SourcePartition();
            this.sources.Add(source, partition);
        }

        return partition;
    }

    private sealed class SourcePartition
    {
        public Dictionary<WorkKey, WorkRecord> Work { get; } = [];

        public HashSet<WorkRecord> Tracked { get; } = [];

        public SourceGeneration Generation { get; set; }

        public DateTimeOffset LastSnapshotAt { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset? LastSucceededAt { get; set; }

        public long ListingStamp { get; set; }

        public SourceState? Health { get; set; }

        public DisconnectReason? Disconnect { get; set; }

        public CapabilityReport? Admitted { get; set; }

        public string? LastKnownVersion { get; set; }

        public bool VersionChanged { get; set; }
    }
}

internal sealed class WorkRecord(WorkKey key, DateTimeOffset firstSeenAt)
{
    public WorkKey Key { get; } = key;

    public WorkState? State { get; set; }

    public ReasonCode Reason { get; set; }

    public Channel SetBy { get; set; }

    public DateTimeOffset StateSince { get; set; }

    public SourceGeneration StateGeneration { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = firstSeenAt;

    public DateTimeOffset LastConfirmedAt { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }

    public long ListingStamp { get; set; }

    public bool EverLive { get; set; }

    public bool SessionEndSeen { get; set; }

    public ProcessIncarnation? LastIncarnation { get; set; }

    public Liveness.ProcessFate LastFate { get; set; } = Liveness.ProcessFate.Unknown;

    public ImmutableArray<Association> Incarnations { get; set; } = [];

    public OwnershipEvidence Ownership { get; set; }

    public ControlReference? Reference { get; set; }

    public Entrypoint Entrypoint { get; set; }

    public ProjectRef Project { get; set; } = ProjectRef.Unassigned;

    public WorkKey? Parent { get; set; }

    public SourceLifecycle Lifecycle { get; set; }

    public WaitingFor WaitingFor { get; set; }

    public DateTimeOffset? SourceReportedAt { get; set; }

    public Liveness.Prior Prior => new(this.State, this.Reason, this.StateSince, this.FirstSeenAt, this.EverLive, this.SessionEndSeen, this.LastIncarnation);

    public void StartNewLife(DateTimeOffset at)
    {
        this.FirstSeenAt = at;
        this.EverLive = false;
        this.SessionEndSeen = false;
        this.Ownership = OwnershipEvidence.None;
    }

    public void Confirm(ListedWork entry, DateTimeOffset at, Liveness.ProcessFate lastFate)
    {
        this.LastConfirmedAt = at;
        this.Entrypoint = entry.Entrypoint;
        this.LastFate = lastFate;
        if (entry is not ListedWork.RecognizedEntry listed)
        {
            return;
        }

        this.Lifecycle = listed.Lifecycle;
        this.WaitingFor = listed.WaitingFor;
        this.Reference = listed.Reference;
        this.Parent = listed.Parent;
        this.Project = listed.Project;
        this.SourceReportedAt = listed.SourceReportedAt;
        if (listed.Ownership != OwnershipEvidence.None)
        {
            this.Ownership = listed.Ownership;
        }

        if (listed.Incarnation is { } incarnation)
        {
            this.EverLive = true;
            this.LastFate = Liveness.ProcessFate.Alive;
            if (incarnation != this.LastIncarnation)
            {
                this.Incarnations = this.Incarnations.Add(new Association(incarnation, Channel.Poll, at));
                this.LastIncarnation = incarnation;
                this.SessionEndSeen = false;
            }
        }
    }
}

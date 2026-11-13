namespace Workbench.Core.Activity.Contract;

using System.Collections.Immutable;
using System.Diagnostics;

public enum ApplyOutcome
{
    Applied,
    IgnoredOlderGeneration,
    IgnoredOutOfOrder,
}

// A work item as projected at the instant of the read, with staleness already applied.
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

    // The record's own immutable list, shared by reads with no change between them, so view equality detects change.
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

// The one owner of Activity's facts in CA1: per-source partitions of projected work, generation and health, and
// admitted capability reports. Adapters send evidence and only this class decides state, through the Liveness table.
// Single writer, not thread-safe: Activity serializes every adapter's batches through one loop. Apply costs
// O(batch + tracked items of that source); ended work leaves the tracked set, so history never enters the hot path.
public sealed class ActivityLedger
{
    private readonly LivenessSettings settings;
    private readonly TimeProvider clock;
    private readonly Dictionary<SourceInstanceId, SourcePartition> sources = [];

    public ActivityLedger(LivenessSettings settings, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        this.settings = settings;
        this.clock = clock;
    }

    // Strictly newer than any generation this ledger issued or applied for the source, so adapter restarts increment by construction (RS-1).
    public SourceGeneration StartGeneration(SourceInstanceId source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var partition = this.PartitionOf(source);
        partition.Issued = new SourceGeneration(Math.Max(partition.Issued.Value, partition.Generation.Value) + 1);
        return partition.Issued;
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
            AdvanceGeneration(partition, batch);
        }
        else if (batch is not SafeSourceBatch.HookDelivery && batch.ObservedAt < partition.LastSnapshotAt)
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

    // Once a source reports a version different from the last known one, every control reads VersionChanged and
    // observation levels stay as reported. Only conformance on the new version could clear it, and CA5 owns that.
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

    public WorkView? Find(WorkKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return this.sources.TryGetValue(key.Source, out var partition) && partition.Work.TryGetValue(key, out var record)
            ? this.Project(record, this.clock.GetUtcNow())
            : null;
    }

    // Every work item of one source, history included.
    public IReadOnlyList<WorkView> Work(SourceInstanceId source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var now = this.clock.GetUtcNow();
        return this.sources.TryGetValue(source, out var partition) ? [.. partition.Work.Values.Select(record => this.Project(record, now))] : [];
    }

    // Null until the source's first poll, failure or unavailability batch.
    public SourceView? Source(SourceInstanceId source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!this.sources.TryGetValue(source, out var partition) || partition.Health is not { } health)
        {
            return null;
        }

        var silent = health == SourceState.Connected && this.clock.GetUtcNow() - partition.LastSucceededAt > this.settings.StaleAfter;
        return silent
            ? new SourceView(source, partition.Generation, SourceState.Disconnected, DisconnectReason.Timeout, partition.LastSucceededAt)
            : new SourceView(source, partition.Generation, health, partition.Disconnect, partition.LastSucceededAt);
    }

    // A tracked command was listed by an older connection, so its identity dies with that connection (CX-2).
    private static void AdvanceGeneration(SourcePartition partition, SafeSourceBatch batch)
    {
        partition.Generation = batch.Generation;
        partition.LastSnapshotAt = DateTimeOffset.MinValue;
        foreach (var record in partition.Tracked)
        {
            if (record.Key.Connection is not null)
            {
                Transit(record, Signal.ConnectionLost, batch);
            }
        }

        partition.Tracked.RemoveWhere(static record => record.Key.Connection is not null);
    }

    private static void ApplySourceWide(SourcePartition partition, SafeSourceBatch batch, Signal signal, SourceState health, DisconnectReason? disconnect)
    {
        foreach (var record in partition.Tracked)
        {
            Transit(record, signal, batch);
        }

        partition.LastSnapshotAt = batch.ObservedAt;
        partition.Health = health;
        partition.Disconnect = disconnect;
    }

    // Hooks never create work, and a late event never overrides a newer observation of the same item.
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
                    record.LastConfirmedAt = delivery.ObservedAt;
                    if (Liveness.ClassifyHook(notification.WaitingFor) is { } signal)
                    {
                        Transit(record, signal, delivery);
                    }

                    break;
            }
        }
    }

    private static void Transit(WorkRecord record, Signal signal, SafeSourceBatch batch)
    {
        record.LastObservedAt = batch.ObservedAt;
        if (Liveness.Step(record.State, signal) is not { } step)
        {
            return;
        }

        if (step.To != record.State)
        {
            record.State = step.To;
            record.StateSince = batch.ObservedAt;
        }

        record.Reason = step.Reason;
        record.SetBy = batch is SafeSourceBatch.HookDelivery ? Channel.Hook : Channel.Poll;
        record.StateGeneration = batch.Generation;
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
            Transit(record, signal, poll);
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
                Transit(record, Liveness.ClassifyUnlisted(record.Prior, record.LastFate), poll);
            }
        }

        partition.Tracked.RemoveWhere(static record => record.State == WorkState.Ended);
        partition.LastSnapshotAt = poll.ObservedAt;
        partition.LastSucceededAt = poll.ObservedAt;
        partition.Health = SourceState.Connected;
        partition.Disconnect = null;
    }

    private WorkView Project(WorkRecord record, DateTimeOffset now)
    {
        var stored = record.State ?? throw new UnreachableException();
        return now - record.LastConfirmedAt > this.settings.StaleAfter && Liveness.Step(stored, Signal.ConfirmationExpired) is { } stale
            ? new WorkView(record, stale.To, stale.Reason, record.LastConfirmedAt + this.settings.StaleAfter)
            : new WorkView(record, stored, record.Reason, record.StateSince);
    }

    private SourcePartition PartitionOf(SourceInstanceId source)
    {
        if (!this.sources.TryGetValue(source, out var partition))
        {
            partition = new SourcePartition();
            this.sources.Add(source, partition);
        }

        return partition;
    }

    // One source instance's projection. Only that source's batches write it, so CX-4's separation is structural.
    private sealed class SourcePartition
    {
        public Dictionary<WorkKey, WorkRecord> Work { get; } = [];

        // Work that is not Ended: the only set absence, disconnect and reconnect walks visit.
        public HashSet<WorkRecord> Tracked { get; } = [];

        public SourceGeneration Generation { get; set; }

        public SourceGeneration Issued { get; set; }

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

// Mutable per-item facts. Only ActivityLedger writes them, and they never leave Core except through WorkView.
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

    // A relisted ended item starts a new life: its earlier liveness and SessionEnd no longer apply.
    public void StartNewLife(DateTimeOffset at)
    {
        this.FirstSeenAt = at;
        this.EverLive = false;
        this.SessionEndSeen = false;
    }

    // Ownership is retained across a stop and bound to this exact key; a listing without it never revokes it.
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

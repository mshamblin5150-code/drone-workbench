namespace Workbench.Core.Activity.Contract;

using System.Collections.Immutable;

public enum ListedStatus
{
    None,
    Busy,
    Waiting,
    Idle,
    NotLoaded,
}

public enum SourceLifecycle
{
    None,
    Working,
    Blocked,
    Done,
    Failed,
    Stopped,
}

public enum WaitingFor
{
    None,
    PermissionPrompt,
    IdlePrompt,
    AgentNeedsInput,
    ElicitationDialog,
    Unknown,
}

public enum DisconnectReason
{
    Timeout,
    NonZeroExit,
    MalformedOutput,
    OutputTooLarge,
    TransportError,
    SchemaDrift,
}

// An abstract record would let another assembly derive through its protected copy constructor, so this is a class with a private constructor.
public abstract class SafeSourceBatch
{
    private SafeSourceBatch(SourceInstanceId source, SourceGeneration generation, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (generation == default)
        {
            throw Grammar.Invalid(nameof(generation));
        }

        Grammar.RequireUtc(observedAt, nameof(observedAt));
        this.Source = source;
        this.Generation = generation;
        this.ObservedAt = observedAt;
    }

    public SourceInstanceId Source { get; }

    public SourceGeneration Generation { get; }

    public DateTimeOffset ObservedAt { get; }

    public static SafeSourceBatch Polled(
        SourceInstanceId source,
        SourceGeneration generation,
        DateTimeOffset observedAt,
        IEnumerable<ListedWork> listing,
        IEnumerable<ProcessIncarnation> liveProcesses,
        bool processInventoryComplete) =>
        new PollSnapshot(source, generation, observedAt, [.. listing], [.. liveProcesses], processInventoryComplete);

    public static SafeSourceBatch Failed(SourceInstanceId source, SourceGeneration generation, DateTimeOffset attemptedAt, DisconnectReason reason)
    {
        Grammar.RequireDefined(reason, nameof(reason));
        return new PollFailure(source, generation, attemptedAt, reason);
    }

    public static SafeSourceBatch Unavailable(SourceInstanceId source, SourceGeneration generation, DateTimeOffset attemptedAt) =>
        new SourceAbsent(source, generation, attemptedAt);

    public static SafeSourceBatch Hooks(SourceInstanceId source, SourceGeneration generation, DateTimeOffset receivedAt, IEnumerable<HookEvent> events) =>
        new HookDelivery(source, generation, receivedAt, [.. events]);

    internal sealed class PollSnapshot : SafeSourceBatch
    {
        internal PollSnapshot(
            SourceInstanceId source,
            SourceGeneration generation,
            DateTimeOffset observedAt,
            ImmutableArray<ListedWork> listing,
            HashSet<ProcessIncarnation> liveProcesses,
            bool processInventoryComplete)
            : base(source, generation, observedAt)
        {
            if (liveProcesses.Contains(default))
            {
                throw Grammar.Invalid(nameof(liveProcesses));
            }

            var keys = new HashSet<WorkKey>();
            foreach (var item in listing)
            {
                ArgumentNullException.ThrowIfNull(item, nameof(listing));
                var foreignConnection = item.Work.Connection is { } connection && connection != generation;
                var unknownProcess = item is ListedWork.RecognizedEntry { Incarnation: { } incarnation } && !liveProcesses.Contains(incarnation);
                if (item.Work.Source != source || !keys.Add(item.Work) || foreignConnection || unknownProcess)
                {
                    throw Grammar.Invalid(nameof(listing));
                }
            }

            this.Listing = listing;
            this.LiveProcesses = liveProcesses;
            this.ProcessInventoryComplete = processInventoryComplete;
        }

        internal ImmutableArray<ListedWork> Listing { get; }

        internal IReadOnlySet<ProcessIncarnation> LiveProcesses { get; }

        internal bool ProcessInventoryComplete { get; }
    }

    internal sealed class PollFailure(SourceInstanceId source, SourceGeneration generation, DateTimeOffset attemptedAt, DisconnectReason reason)
        : SafeSourceBatch(source, generation, attemptedAt)
    {
        internal DisconnectReason Reason { get; } = reason;
    }

    internal sealed class SourceAbsent(SourceInstanceId source, SourceGeneration generation, DateTimeOffset attemptedAt)
        : SafeSourceBatch(source, generation, attemptedAt);

    internal sealed class HookDelivery : SafeSourceBatch
    {
        internal HookDelivery(SourceInstanceId source, SourceGeneration generation, DateTimeOffset receivedAt, ImmutableArray<HookEvent> events)
            : base(source, generation, receivedAt)
        {
            if (events.Any(hook => hook is null || hook.Session.Source != source))
            {
                throw Grammar.Invalid(nameof(events));
            }

            this.Events = events;
        }

        internal ImmutableArray<HookEvent> Events { get; }
    }
}

public abstract class ListedWork
{
    private ListedWork(WorkKey work, Entrypoint entrypoint)
    {
        ArgumentNullException.ThrowIfNull(work);
        Grammar.RequireDefined(entrypoint, nameof(entrypoint));
        this.Work = work;
        this.Entrypoint = entrypoint;
    }

    internal WorkKey Work { get; }

    internal Entrypoint Entrypoint { get; }

    public static ListedWork Entry(
        WorkKey work,
        Entrypoint entrypoint,
        ListedStatus status = ListedStatus.None,
        SourceLifecycle lifecycle = SourceLifecycle.None,
        ProcessIncarnation? incarnation = null,
        WaitingFor waitingFor = WaitingFor.None,
        OwnershipEvidence ownership = OwnershipEvidence.None,
        ControlReference? reference = null,
        WorkKey? parent = null,
        ProjectRef? project = null,
        DateTimeOffset? sourceReportedAt = null) =>
        new RecognizedEntry(work, entrypoint, status, lifecycle, incarnation, waitingFor, ownership, reference, parent, project, sourceReportedAt);

    public static ListedWork Unrecognized(WorkKey work, Entrypoint entrypoint = Entrypoint.Unknown) => new UnrecognizedEntry(work, entrypoint);

    internal sealed class RecognizedEntry : ListedWork
    {
        internal RecognizedEntry(
            WorkKey work,
            Entrypoint entrypoint,
            ListedStatus status,
            SourceLifecycle lifecycle,
            ProcessIncarnation? incarnation,
            WaitingFor waitingFor,
            OwnershipEvidence ownership,
            ControlReference? reference,
            WorkKey? parent,
            ProjectRef? project,
            DateTimeOffset? sourceReportedAt)
            : base(work, entrypoint)
        {
            Grammar.RequireDefined(status, nameof(status));
            Grammar.RequireDefined(lifecycle, nameof(lifecycle));
            Grammar.RequireDefined(waitingFor, nameof(waitingFor));
            Grammar.RequireDefined(ownership, nameof(ownership));
            if (incarnation == default(ProcessIncarnation))
            {
                throw Grammar.Invalid(nameof(incarnation));
            }

            if (reference is not null && reference.Target != work)
            {
                throw Grammar.Invalid(nameof(reference));
            }

            if (parent is not null && (parent.Source != work.Source || parent == work))
            {
                throw Grammar.Invalid(nameof(parent));
            }

            if (status == ListedStatus.NotLoaded && work.Source.Kind == SourceKind.Claude)
            {
                throw new ArgumentException("Claude sources have no stored-but-not-loaded work.", nameof(status));
            }

            this.Status = status;
            this.Lifecycle = lifecycle;
            this.Incarnation = incarnation;
            this.WaitingFor = waitingFor;
            this.Ownership = ownership;
            this.Reference = reference;
            this.Parent = parent;
            this.Project = project ?? ProjectRef.Unassigned;
            this.SourceReportedAt = sourceReportedAt;
        }

        internal ListedStatus Status { get; }

        internal SourceLifecycle Lifecycle { get; }

        internal ProcessIncarnation? Incarnation { get; }

        internal WaitingFor WaitingFor { get; }

        internal OwnershipEvidence Ownership { get; }

        internal ControlReference? Reference { get; }

        internal WorkKey? Parent { get; }

        internal ProjectRef Project { get; }

        internal DateTimeOffset? SourceReportedAt { get; }
    }

    internal sealed class UnrecognizedEntry(WorkKey work, Entrypoint entrypoint) : ListedWork(work, entrypoint);
}

public abstract class HookEvent
{
    private HookEvent(WorkKey session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Kind != WorkKind.Session)
        {
            throw Grammar.Invalid(nameof(session));
        }

        this.Session = session;
    }

    internal WorkKey Session { get; }

    public static HookEvent SessionEnd(WorkKey session) => new SessionEndEvent(session);

    public static HookEvent Notification(WorkKey session, WaitingFor waitingFor)
    {
        Grammar.RequireDefined(waitingFor, nameof(waitingFor));
        return new NotificationEvent(session, waitingFor);
    }

    internal sealed class SessionEndEvent(WorkKey session) : HookEvent(session);

    internal sealed class NotificationEvent(WorkKey session, WaitingFor waitingFor) : HookEvent(session)
    {
        internal WaitingFor WaitingFor { get; } = waitingFor;
    }
}

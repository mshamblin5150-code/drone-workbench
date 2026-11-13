namespace Workbench.Core.Activity.Contract;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

// A classified source refusal. Provider repeat-control facts keep this classification and never become a Workbench
// action or acknowledgement (CX-3).
public enum SourceRejection
{
    NoMatchingJob,
    NoActiveCommand,
    Unclassified,
}

public enum ExitObservation
{
    Exited,
    StillRunning,
    NotObserved,
}

// The one seam between a source adapter and Activity. Observation-only adapters implement the first three members.
// Nothing can reach the control members in CA1: Activity builds a VerifiedControl only for an enabled capability,
// and CA1 cannot represent one.
public interface ISourceAdapter
{
    SourceKind Kind { get; }

    Task<CapabilityReport> DescribeAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<SafeSourceBatch> ObserveAsync(CancellationToken cancellationToken);

    Task<ControlDispatch> DispatchAsync(VerifiedControl control, CancellationToken cancellationToken) =>
        Task.FromException<ControlDispatch>(new NotSupportedException("This adapter has no control route."));

    Task<ControlEvidence> ReconcileAsync(ControlReceiptRef receipt, CancellationToken cancellationToken) =>
        Task.FromException<ControlEvidence>(new NotSupportedException("This adapter has no control route."));
}

// Process exit for one recorded incarnation, kept separate from the source outcome: neither implies the other.
public sealed record ProcessExit(ProcessIncarnation Incarnation, ExitObservation Observation, DateTimeOffset ObservedAt);

// Dispatch authority. Only Activity can build one, after its own checks, so an adapter can receive authority but never
// mint it. It binds the exact work, generation, expected incarnation and required state for atomic source enforcement.
public sealed class VerifiedControl
{
    private readonly ControlReference reference;

    internal VerifiedControl(
        RequestId request,
        ControlAction action,
        ControlReference reference,
        SourceGeneration generation,
        ProcessIncarnation? expectedIncarnation,
        WorkState requiredState)
    {
        this.Request = request;
        this.Action = action;
        this.reference = reference;
        this.Generation = generation;
        this.ExpectedIncarnation = expectedIncarnation;
        this.RequiredState = requiredState;
    }

    public RequestId Request { get; }

    public ControlAction Action { get; }

    public WorkKey Target => this.reference.Target;

    public SourceGeneration Generation { get; }

    public ProcessIncarnation? ExpectedIncarnation { get; }

    public WorkState RequiredState { get; }

    // A source restart invalidates generation-bound authority for both reference scopes.
    public bool TryOpenHandle(SourceGeneration liveConnection, [NotNullWhen(true)] out string? handle)
    {
        handle = liveConnection == this.Generation && this.reference.IsUsableAt(liveConnection) ? this.reference.Handle : null;
        return handle is not null;
    }
}

public sealed class ControlDispatch
{
    private ControlDispatch(ReceiptStage stage, SourceRejection? rejection, DateTimeOffset observedAt)
    {
        Grammar.RequireUtc(observedAt, nameof(observedAt));
        this.Stage = stage;
        this.Rejection = rejection;
        this.ObservedAt = observedAt;
    }

    public ReceiptStage Stage { get; }

    public SourceRejection? Rejection { get; }

    public DateTimeOffset ObservedAt { get; }

    public static ControlDispatch Acknowledged(DateTimeOffset observedAt) => new(ReceiptStage.SourceAcknowledged, null, observedAt);

    public static ControlDispatch Rejected(SourceRejection reason, DateTimeOffset observedAt)
    {
        Grammar.RequireDefined(reason, nameof(reason));
        return new(ReceiptStage.SourceRejected, reason, observedAt);
    }

    public static ControlDispatch DeliveryUnknown(DateTimeOffset observedAt) => new(ReceiptStage.DeliveryUnknown, null, observedAt);
}

public sealed class ControlEvidence
{
    private ControlEvidence(ReceiptStage stage, IEnumerable<ProcessExit> exits, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(exits);
        Grammar.RequireUtc(observedAt, nameof(observedAt));
        this.Stage = stage;
        this.Exits = [.. exits];
        this.ObservedAt = observedAt;
    }

    public ReceiptStage Stage { get; }

    public ImmutableArray<ProcessExit> Exits { get; }

    public DateTimeOffset ObservedAt { get; }

    public static ControlEvidence Verified(IEnumerable<ProcessExit> exits, DateTimeOffset observedAt) => new(ReceiptStage.OutcomeVerified, exits, observedAt);

    public static ControlEvidence NotObserved(IEnumerable<ProcessExit> exits, DateTimeOffset observedAt) => new(ReceiptStage.OutcomeNotObserved, exits, observedAt);

    public static ControlEvidence Contradicted(IEnumerable<ProcessExit> exits, DateTimeOffset observedAt) => new(ReceiptStage.OutcomeContradicted, exits, observedAt);
}

// What an adapter needs to reconcile a dispatched receipt. Only Activity creates one.
public sealed class ControlReceiptRef
{
    internal ControlReceiptRef(
        RequestId request,
        ControlAction action,
        WorkKey target,
        SourceGeneration generation,
        ProcessIncarnation? expectedIncarnation,
        DateTimeOffset dispatchedAt)
    {
        this.Request = request;
        this.Action = action;
        this.Target = target;
        this.Generation = generation;
        this.ExpectedIncarnation = expectedIncarnation;
        this.DispatchedAt = dispatchedAt;
    }

    public RequestId Request { get; }

    public ControlAction Action { get; }

    public WorkKey Target { get; }

    public SourceGeneration Generation { get; }

    public ProcessIncarnation? ExpectedIncarnation { get; }

    public DateTimeOffset DispatchedAt { get; }
}

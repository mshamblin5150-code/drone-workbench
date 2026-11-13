namespace Workbench.Core.Activity.Contract;

using System.Collections.Frozen;
using System.Collections.Immutable;

// The host sets this from the authenticated route, never from submitted fields. Only Operator may request a stop.
public enum CallerScope
{
    Operator,
    Drone,
    Cli,
}

public enum ReceiptStage
{
    Requested,
    Refused,
    NoActionNeeded,
    Accepted,
    Dispatched,
    SourceAcknowledged,
    SourceRejected,
    DeliveryUnknown,
    OutcomeVerified,
    OutcomeNotObserved,
    OutcomeContradicted,
}

public enum RefusalReason
{
    NoSupportedRoute,
    NoConditionalDispatch,
    NoConformance,
    VersionChanged,
    CallerScope,
    UnknownTarget,
    StaleTarget,
    IncarnationChanged,
    NoOwnershipEvidence,
    TargetConditionUnverified,
    RequestIdConflict,
}

// A client-chosen request identity. The same id returns the same receipt.
public readonly record struct RequestId
{
    public RequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw Grammar.Invalid(nameof(value));
        }

        this.Value = value;
    }

    public Guid Value { get; }
}

public readonly record struct ReceiptStep(ReceiptStage Stage, DateTimeOffset ObservedAt);

// Only the ledger creates and advances a receipt, and every advance goes through ReceiptEdges. NoActionNeeded
// always means AlreadyStopped, the contract's only no-op reason, so it carries no separate reason value.
public sealed class ControlReceipt
{
    internal ControlReceipt(RequestId request, ControlAction action, WorkKey target, ProcessIncarnation? seenIncarnation, CallerScope caller, DateTimeOffset requestedAt)
        : this(request, action, target, seenIncarnation, caller, [new ReceiptStep(ReceiptStage.Requested, requestedAt)], null)
    {
    }

    private ControlReceipt(
        RequestId request,
        ControlAction action,
        WorkKey target,
        ProcessIncarnation? seenIncarnation,
        CallerScope caller,
        ImmutableArray<ReceiptStep> history,
        RefusalReason? refusal)
    {
        this.Request = request;
        this.Action = action;
        this.Target = target;
        this.SeenIncarnation = seenIncarnation;
        this.Caller = caller;
        this.History = history;
        this.Refusal = refusal;
    }

    public RequestId Request { get; }

    public ControlAction Action { get; }

    public WorkKey Target { get; }

    public ProcessIncarnation? SeenIncarnation { get; }

    public CallerScope Caller { get; }

    public ImmutableArray<ReceiptStep> History { get; }

    public ReceiptStage Stage => this.History[^1].Stage;

    public RefusalReason? Refusal { get; }

    // Views say "already stopped" whenever this is false, never "Workbench stopped it" (CT-3, CX-3).
    public bool WorkbenchDispatched => this.History.Any(step => step.Stage == ReceiptStage.Dispatched);

    // A refusal reason is required exactly when the next stage is Refused.
    internal ControlReceipt Advance(ReceiptStage next, DateTimeOffset at, RefusalReason? refusal = null)
    {
        if (!ReceiptEdges.Allows(this.Stage, next))
        {
            throw new InvalidOperationException($"The contract has no receipt edge from {this.Stage} to {next}.");
        }

        if ((next == ReceiptStage.Refused) != refusal.HasValue)
        {
            throw new ArgumentException("Only a refusal carries a refusal reason, and every refusal carries one.", nameof(refusal));
        }

        return new(this.Request, this.Action, this.Target, this.SeenIncarnation, this.Caller, this.History.Add(new(next, at)), refusal);
    }
}

// The contract's receipt stage edges. Nothing else decides receipt order.
internal static class ReceiptEdges
{
    private static readonly ImmutableArray<(ReceiptStage From, ReceiptStage To)> Edges =
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

    private static readonly FrozenSet<(ReceiptStage From, ReceiptStage To)> Allowed = Edges.ToFrozenSet();

    internal static ImmutableArray<(ReceiptStage From, ReceiptStage To)> All => Edges;

    internal static bool Allows(ReceiptStage from, ReceiptStage to) => Allowed.Contains((from, to));
}

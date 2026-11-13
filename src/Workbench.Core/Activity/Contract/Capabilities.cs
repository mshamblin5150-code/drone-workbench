namespace Workbench.Core.Activity.Contract;

using System.Collections.Immutable;

public enum Entrypoint
{
    Desktop,
    InteractiveCli,
    Background,
    Headless,
    Ide,
    Cloud,
    Unknown,
}

public enum CoverageLevel
{
    Verified,
    Observed,
    Partial,
    Unverified,
    Unsupported,
    Unreachable,
}

public enum Channel
{
    Poll,
    ProcessScan,
    Hook,
    Publication,
    ControlResult,
}

public enum ControlAction
{
    StopBackgroundSession,
    TerminateOwnedCommand,
    InterruptTurn,
}

// Observation gaps only. Control-route gaps are DisabledReason values, so each reason lives in exactly one enum.
public enum GapReason
{
    NoSupportedEndpoint,
    UnrecognizedLaunchPath,
    NotInAgentView,
    AwaitingAuthorization,
    NotInventoried,
    NotTested,
}

// A set, because a version change disables a control that was already disabled for its route: both reasons stay visible.
[Flags]
public enum DisabledReason
{
    None = 0,
    NoSupportedRoute = 1,
    NoConditionalDispatch = 2,
    NoConformance = 4,
    VersionChanged = 8,
}

public sealed record CapabilityReport
{
    public CapabilityReport(
        SourceInstanceId source,
        SourceGeneration generation,
        string? sourceVersion,
        SourceState state,
        IReadOnlyList<EntrypointCoverage> entrypoints,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entrypoints);
        if (generation == default)
        {
            throw Grammar.Invalid(nameof(generation));
        }

        Grammar.RequireDefined(state, nameof(state));
        Grammar.RequireUtc(observedAt, nameof(observedAt));
        if (sourceVersion is not null)
        {
            Grammar.RequireVersion(sourceVersion, nameof(sourceVersion));
        }

        if (entrypoints.DistinctBy(coverage => coverage.Entrypoint).Count() != entrypoints.Count)
        {
            throw Grammar.Invalid(nameof(entrypoints));
        }

        if (entrypoints.Any(coverage => coverage.Observation == CoverageLevel.Verified &&
            (sourceVersion is null || coverage.Evidence.SourceVersion != sourceVersion)))
        {
            throw new ArgumentException("Verified coverage needs conformance evidence on the reported source version.", nameof(entrypoints));
        }

        this.Source = source;
        this.Generation = generation;
        this.SourceVersion = sourceVersion;
        this.State = state;
        this.Entrypoints = [.. entrypoints];
        this.ObservedAt = observedAt;
    }

    public SourceInstanceId Source { get; }

    public SourceGeneration Generation { get; }

    // Null when the source could not be asked, such as an unreachable Codex desktop owner.
    public string? SourceVersion { get; }

    public SourceState State { get; }

    public ImmutableArray<EntrypointCoverage> Entrypoints { get; private init; }

    public DateTimeOffset ObservedAt { get; }

    internal CapabilityReport WithControlsDisabled(DisabledReason reason) =>
        this with { Entrypoints = [.. this.Entrypoints.Select(coverage => coverage.WithControlsDisabled(reason))] };
}

public sealed record EntrypointCoverage
{
    // A count of processes no listing covers (CV-5, CX-1). Null when the inventory was incomplete, so the total is unknown rather than zero.
    public EntrypointCoverage(
        Entrypoint entrypoint,
        CoverageLevel observation,
        IReadOnlyList<Channel> channels,
        IReadOnlyList<WorkKind> workKinds,
        IReadOnlyList<ControlCapability> controls,
        EvidenceRef evidence,
        GapReason? gap = null,
        int? unlistedProcessCount = null)
    {
        Grammar.RequireDefined(entrypoint, nameof(entrypoint));
        Grammar.RequireDefined(observation, nameof(observation));
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(workKinds);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(evidence);
        if (gap is { } reason)
        {
            Grammar.RequireDefined(reason, nameof(gap));
        }

        var covered = observation is CoverageLevel.Verified or CoverageLevel.Observed or CoverageLevel.Partial;
        if (!covered && gap is null)
        {
            throw new ArgumentException("An uncovered entrypoint names its gap.", nameof(gap));
        }

        if ((observation == CoverageLevel.Verified && evidence.Supports != CoverageLevel.Verified) ||
            (covered && evidence.Supports is not (CoverageLevel.Verified or CoverageLevel.Observed)))
        {
            throw new ArgumentException("The evidence does not support this coverage level.", nameof(evidence));
        }

        if (unlistedProcessCount < 0 || controls.DistinctBy(control => control.Action).Count() != controls.Count)
        {
            throw Grammar.Invalid(unlistedProcessCount < 0 ? nameof(unlistedProcessCount) : nameof(controls));
        }

        this.Entrypoint = entrypoint;
        this.Observation = observation;
        this.Channels = [.. channels];
        this.WorkKinds = [.. workKinds];
        this.Controls = [.. controls];
        this.Evidence = evidence;
        this.Gap = gap;
        this.UnlistedProcessCount = unlistedProcessCount;
    }

    public Entrypoint Entrypoint { get; }

    public CoverageLevel Observation { get; }

    public ImmutableArray<Channel> Channels { get; }

    public ImmutableArray<WorkKind> WorkKinds { get; }

    public ImmutableArray<ControlCapability> Controls { get; private init; }

    public EvidenceRef Evidence { get; }

    public GapReason? Gap { get; }

    public int? UnlistedProcessCount { get; }

    internal EntrypointCoverage WithControlsDisabled(DisabledReason reason) =>
        this with { Controls = [.. this.Controls.Select(control => control.WithDisabled(reason))] };
}

// CA1 has no factory for an enabled control, so every control stays disabled by construction. CA5 adds enablement with conformance evidence.
public sealed record ControlCapability
{
    private const DisabledReason AllReasons =
        DisabledReason.NoSupportedRoute | DisabledReason.NoConditionalDispatch | DisabledReason.NoConformance | DisabledReason.VersionChanged;

    private ControlCapability(ControlAction action, WorkKind target, DisabledReason disabled, EvidenceRef evidence)
    {
        this.Action = action;
        this.Target = target;
        this.Disabled = disabled;
        this.Evidence = evidence;
    }

    public ControlAction Action { get; }

    public WorkKind Target { get; }

    public ImmutableArray<WorkState> VerifiedFor { get; } = [];

    public bool Enabled => this.Disabled == DisabledReason.None;

    public DisabledReason Disabled { get; }

    public EvidenceRef Evidence { get; }

    public static ControlCapability Disable(ControlAction action, WorkKind target, DisabledReason reason, EvidenceRef evidence)
    {
        Grammar.RequireDefined(action, nameof(action));
        Grammar.RequireDefined(target, nameof(target));
        ArgumentNullException.ThrowIfNull(evidence);
        if (reason == DisabledReason.None || (reason & ~AllReasons) != 0)
        {
            throw Grammar.Invalid(nameof(reason));
        }

        return new(action, target, reason, evidence);
    }

    internal ControlCapability WithDisabled(DisabledReason reason) => new(this.Action, this.Target, this.Disabled | reason, this.Evidence);
}

// Supports is the highest coverage the evidence can justify: conformance on one version gives Verified, research gives Observed.
public sealed record EvidenceRef
{
    private EvidenceRef(CoverageLevel supports, string? id, string? sourceVersion)
    {
        this.Supports = supports;
        this.Id = id;
        this.SourceVersion = sourceVersion;
    }

    public static EvidenceRef None { get; } = new(CoverageLevel.Unverified, null, null);

    public CoverageLevel Supports { get; }

    public string? Id { get; }

    public string? SourceVersion { get; }

    public static EvidenceRef Documentation(string recordId) => new(CoverageLevel.Unverified, Grammar.RequireSlug(recordId, nameof(recordId)), null);

    public static EvidenceRef Research(string recordId) => new(CoverageLevel.Observed, Grammar.RequireSlug(recordId, nameof(recordId)), null);

    public static EvidenceRef Conformance(string receiptId, string sourceVersion) =>
        new(CoverageLevel.Verified, Grammar.RequireSlug(receiptId, nameof(receiptId)), Grammar.RequireVersion(sourceVersion, nameof(sourceVersion)));
}

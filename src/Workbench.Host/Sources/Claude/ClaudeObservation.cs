namespace Workbench.Host.Sources.Claude;

internal sealed record ClaudeObservation(
    ClaudeObservation.CaptureState State,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? ObservedAt,
    string? SourceVersion,
    ClaudeObservation.RootScope ConfigurationScope,
    IReadOnlyList<ClaudeObservation.SessionSummary>? Sessions,
    ClaudeObservation.ProcessTotals ProcessCoverage,
    ClaudeObservation.FailureReason Failure,
    IReadOnlyList<ClaudeObservation.CoverageGap> CoverageGaps)
{
    public enum CaptureState
    {
        Observed,
        Partial,
        Unavailable,
        Disconnected,
        UnsupportedVersion,
    }

    public enum RootScope
    {
        DefaultRoot,
        ConfiguredRoot,
    }

    public enum Entrypoint
    {
        Desktop,
        InteractiveCli,
        Background,
        Unknown,
    }

    public enum WorkState
    {
        Busy,
        Waiting,
        Idle,
        Stopped,
        Unknown,
    }

    public enum FailureReason
    {
        None,
        MissingExecutable,
        InvalidConfigurationRoot,
        UnsupportedVersion,
        Timeout,
        OutputTooLarge,
        CommandFailed,
        Cancelled,
        MalformedOutput,
        SchemaDrift,
    }

    public enum CoverageGap
    {
        OtherConfigurationRootsUnverified,
        PostPromptInteractiveUnverified,
        HeadlessAndSubagentsUnverified,
        IdeCloudWslAndElevatedUnverified,
        CodexOwnerUnreachable,
        SupervisorSignalUnavailable,
        ProjectMappingUnavailable,
        ProcessInventoryIncomplete,
        UnknownSessionState,
        SchemaDrift,
    }

    public string SupervisorHealth => "Unknown";

    public bool ControlsAvailable => false;

    internal static IReadOnlyList<CoverageGap> DefaultGaps { get; } =
    [
        CoverageGap.OtherConfigurationRootsUnverified,
        CoverageGap.PostPromptInteractiveUnverified,
        CoverageGap.HeadlessAndSubagentsUnverified,
        CoverageGap.IdeCloudWslAndElevatedUnverified,
        CoverageGap.CodexOwnerUnreachable,
        CoverageGap.SupervisorSignalUnavailable,
        CoverageGap.ProjectMappingUnavailable,
    ];

    public sealed record SessionSummary(Entrypoint Entrypoint, WorkState State, bool ProcessMatched);

    public sealed record ProcessTotals(bool Complete, int KnownUnmatchedCount, int? UnmatchedCount);
}

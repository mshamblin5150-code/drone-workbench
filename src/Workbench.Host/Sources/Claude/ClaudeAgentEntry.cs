namespace Workbench.Host.Sources.Claude;

internal sealed record ClaudeAgentEntry(
    Guid SessionId,
    ClaudeAgentEntry.AgentKind Kind,
    ClaudeObservation.WorkState Status,
    bool Stopped,
    int? ProcessId,
    bool HasUnknownValues)
{
    public enum AgentKind
    {
        Interactive,
        Background,
        Unknown,
    }
}

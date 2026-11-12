namespace Workbench.Host.Sources.Claude;

internal sealed record ClaudeProcessInventory(
    bool Complete,
    IReadOnlyDictionary<int, ClaudeProcessInventory.ProcessIdentity> Processes)
{
    public enum ProcessRole
    {
        Claude,
        DesktopApp,
        Terminal,
        Other,
    }

    public sealed record ProcessIdentity(
        int ProcessId,
        DateTimeOffset? CreatedAt,
        int ParentId,
        ProcessRole Role,
        bool IsClaude);
}

namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Workbench.TestSupport;

internal static class ContractFixture
{
    public const string SessionA = "5e55a001-0000-4000-8000-000000000001";
    public const string ShortA = "5e55a001";
    public const string SessionB = "5e55a002-0000-4000-8000-000000000002";
    public const string ShortB = "5e55a002";
    public const string SessionC = "5e55a003-0000-4000-8000-000000000003";
    public const string ClaudeRoot = @"C:\Users\fixture-person\.claude";

    public static readonly DateTimeOffset Start = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    public static readonly LivenessSettings Settings = new(
        pollInterval: TimeSpan.FromSeconds(2),
        staleAfter: TimeSpan.FromSeconds(10),
        startingWindow: TimeSpan.FromSeconds(15),
        restartWindow: TimeSpan.FromSeconds(30));

    public static byte[] Key { get; } = [.. Enumerable.Range(1, 32).Select(value => (byte)value)];

    public static SourceInstanceId Claude { get; } = SourceInstanceId.Mint(SourceKind.Claude, Key, ClaudeRoot);

    public static SourceInstanceId CodexDesktop { get; } = SourceInstanceId.Mint(SourceKind.Codex, Key, "server-process:desktop-app-server");

    public static SourceInstanceId CodexOwned { get; } = SourceInstanceId.Mint(SourceKind.Codex, Key, "endpoint:workbench-owned-socket");

    public static ManualClock Clock() => new(Start);

    public static ProcessIncarnation Process(int pid, int startedMinutesBefore = 5) => new(pid, Start.AddMinutes(-startedMinutesBefore));

    public static WorkKey Session(string sessionId) => WorkKey.Of(Claude, WorkKind.Session, sessionId);
}

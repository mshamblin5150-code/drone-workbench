namespace Workbench.Core.Tests.Activity.Contract;

using System.Reflection;
using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class SafeSourceBatchTests
{
    private readonly ActivityLedger ledger = new(Settings, Clock());

    [Fact]
    public void APollRejectsForeignWorkDuplicateKeysCommandsOfAnotherConnectionAndProcessesMissingFromTheInventory()
    {
        var claude = this.ledger.StartGeneration(Claude);
        var codex = this.ledger.StartGeneration(CodexOwned);
        var key = Session(SessionA);
        var foreign = WorkKey.Of(CodexOwned, WorkKind.Thread, "thread-1");
        var oldCommand = WorkKey.Command(CodexOwned, codex, "proc-7");
        var live = Process(101);

        AssertRejected(() => SafeSourceBatch.Polled(Claude, claude, Start, [Listed(foreign)], [], true));
        AssertRejected(() => SafeSourceBatch.Polled(Claude, claude, Start, [Listed(key), Listed(key)], [], true));
        AssertRejected(() => SafeSourceBatch.Polled(CodexOwned, this.ledger.StartGeneration(CodexOwned), Start, [Listed(oldCommand)], [], true));
        AssertRejected(() => SafeSourceBatch.Polled(Claude, claude, Start, [ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [], true));
        Assert.NotNull(SafeSourceBatch.Polled(Claude, claude, Start, [ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live], true));
    }

    [Fact]
    public void EveryBatchNeedsAnIssuedGenerationAndAUtcTime()
    {
        var generation = this.ledger.StartGeneration(Claude);

        AssertRejected(() => SafeSourceBatch.Unavailable(Claude, default, Start));
        AssertRejected(() => SafeSourceBatch.Failed(Claude, generation, Start.ToOffset(TimeSpan.FromHours(2)), DisconnectReason.Timeout));
        Assert.Empty(typeof(SourceGeneration).GetConstructors());
        Assert.DoesNotContain(
            typeof(SourceGeneration).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static),
            method => method.ReturnType == typeof(SourceGeneration));
    }

    [Fact]
    public void NotLoadedIsRejectedForClaudeWorkAndAcceptedForCodexWork()
    {
        Assert.Throws<ArgumentException>(() => ListedWork.Entry(Session(SessionA), Entrypoint.Background, ListedStatus.NotLoaded));
        Assert.NotNull(ListedWork.Entry(WorkKey.Of(CodexOwned, WorkKind.Thread, "thread-1"), Entrypoint.Desktop, ListedStatus.NotLoaded));
    }

    [Fact]
    public void AnEntryRejectsAReferenceOrParentThatPointsAtOtherWork()
    {
        var key = Session(SessionA);
        Assert.True(ControlReference.TrySourceStable(Session(SessionB), ShortB, out var otherReference));

        AssertRejected(() => ListedWork.Entry(key, Entrypoint.Background, reference: otherReference));
        AssertRejected(() => ListedWork.Entry(key, Entrypoint.Background, parent: key));
        AssertRejected(() => ListedWork.Entry(key, Entrypoint.Background, parent: WorkKey.Of(CodexOwned, WorkKind.Thread, "thread-1")));
    }

    [Fact]
    public void AHookBatchCarriesOnlyItsOwnSourcesSessions()
    {
        var generation = this.ledger.StartGeneration(CodexOwned);

        AssertRejected(() => SafeSourceBatch.Hooks(CodexOwned, generation, Start, [HookEvent.SessionEnd(Session(SessionA))]));
        AssertRejected(() => HookEvent.SessionEnd(WorkKey.Of(Claude, WorkKind.Turn, "turn-1")));
    }

    private static ListedWork Listed(WorkKey key) => ListedWork.Entry(key, Entrypoint.Background);

    private static void AssertRejected(Func<object> build) => Assert.IsAssignableFrom<ArgumentException>(Record.Exception(build));
}

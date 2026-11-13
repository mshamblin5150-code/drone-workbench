namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Workbench.TestSupport;

internal sealed class LedgerScenario
{
    public LedgerScenario(SourceInstanceId source)
    {
        this.Source = source;
        this.Ledger = new ActivityLedger(ContractFixture.Settings, this.Clock);
        this.Generation = this.Ledger.StartGeneration(source);
    }

    public SourceInstanceId Source { get; }

    public ManualClock Clock { get; } = ContractFixture.Clock();

    public ActivityLedger Ledger { get; }

    public SourceGeneration Generation { get; set; }

    public DateTimeOffset Now => this.Clock.GetUtcNow();

    public ApplyOutcome Poll(ListedWork[] listing, ProcessIncarnation[] live, bool inventoryComplete = true) =>
        this.Apply(SafeSourceBatch.Polled(this.Source, this.Generation, this.Next(), listing, live, inventoryComplete));

    public ApplyOutcome Fail(DisconnectReason reason) => this.Apply(SafeSourceBatch.Failed(this.Source, this.Generation, this.Next(), reason));

    public ApplyOutcome Hooks(params HookEvent[] events) => this.Apply(SafeSourceBatch.Hooks(this.Source, this.Generation, this.Next(), events));

    public ApplyOutcome Apply(SafeSourceBatch batch) => this.Ledger.Apply(batch);

    public WorkView View(WorkKey key) => this.Ledger.Find(key) ?? throw new InvalidOperationException("The ledger has no such work item.");

    public (WorkState State, ReasonCode Reason) StateOf(WorkKey key)
    {
        var view = this.View(key);
        return (view.State, view.Reason);
    }

    private DateTimeOffset Next()
    {
        this.Clock.Advance(ContractFixture.Settings.PollInterval);
        return this.Now;
    }
}

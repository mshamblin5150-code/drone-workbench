namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Workbench.TestSupport;

internal sealed class LedgerScenario
{
    private DateTimeOffset? lastScheduledAt;
    private TimeSpan due = ContractFixture.PollEvery;

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

    public TimeSpan Interval { get; set; } = ContractFixture.PollEvery;

    public DateTimeOffset Now => this.Clock.GetUtcNow();

    public ApplyOutcome Poll(ListedWork[] listing, ProcessIncarnation[] live, bool inventoryComplete = true) =>
        this.Apply(SafeSourceBatch.Polled(this.Source, this.Generation, this.OnSchedule(), this.Interval, listing, live, inventoryComplete));

    public ApplyOutcome Fail(DisconnectReason reason) => this.Apply(SafeSourceBatch.Failed(this.Source, this.Generation, this.OnSchedule(), this.Interval, reason));

    public ApplyOutcome Unavailable() => this.Apply(SafeSourceBatch.Unavailable(this.Source, this.Generation, this.OnSchedule(), this.Interval));

    public ApplyOutcome Hooks(params HookEvent[] events)
    {
        this.Clock.Advance(ContractFixture.PollEvery);
        return this.Apply(SafeSourceBatch.Hooks(this.Source, this.Generation, this.Now, events));
    }

    public ApplyOutcome Apply(SafeSourceBatch batch) => this.Ledger.Apply(batch);

    public WorkView View(WorkKey key) => this.Ledger.Find(key) ?? throw new InvalidOperationException("The ledger has no such work item.");

    public (WorkState State, ReasonCode Reason) StateOf(WorkKey key)
    {
        var view = this.View(key);
        return (view.State, view.Reason);
    }

    private DateTimeOffset OnSchedule()
    {
        var dueAt = (this.lastScheduledAt ?? this.Now) + this.due;
        if (dueAt > this.Now)
        {
            this.Clock.Advance(dueAt - this.Now);
        }

        this.lastScheduledAt = this.Now;
        this.due = this.Interval;
        return this.Now;
    }
}

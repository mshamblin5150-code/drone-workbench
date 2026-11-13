namespace Workbench.Core.Tests.Activity.Contract;

using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Workbench.Core.Activity.Contract;
using Workbench.TestSupport;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

// Opt-in, Release only: dotnet test --project tests/Workbench.Core.Tests -c Release --filter-class "*ReducerBenchmarks" --explicit only
// BenchmarkDotNet runs in process because its out-of-process toolchain does not start on net11.0.
public sealed class ReducerBenchmarks(ITestOutputHelper output)
{
    public enum History
    {
        EndedOnly,
        OnePercentUnresolved,
        OnePercentLost,
    }

    [Fact(Explicit = true)]
    public void ApplyCostFollowsTheBatchAndNotTheHistory()
    {
        PollBenchmarks.Faults.Clear();
        var summary = BenchmarkRunner.Run(
            [typeof(ReducerApply), typeof(SourceView)],
            DefaultConfig.Instance.WithOptions(ConfigOptions.JoinSummary)).Single();
        output.WriteLine($"report {summary.ResultsDirectoryPath}, log {summary.LogFilePath}");

        Assert.Empty(summary.ValidationErrors);
        Assert.Equal(18, summary.Reports.Length);
        Assert.All(summary.Reports, report => Assert.True(report.Success, report.BenchmarkCase.DisplayInfo));
        Assert.Empty(PollBenchmarks.Faults);
        var means = summary.Reports.ToDictionary(Case, report => report.ResultStatistics!.Mean);
        foreach (var report in summary.Reports)
        {
            var statistics = report.ResultStatistics!;
            var error = statistics.ConfidenceInterval.Margin;
            output.WriteLine(
                $"{Case(report)}: mean {statistics.Mean / 1_000:F3} us, error {error / 1_000:F3} us, upper {(statistics.Mean + error) / 1_000:F3} us, " +
                $"allocated {report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase):N0} B/op");
        }

        foreach (var history in new[] { History.EndedOnly, History.OnePercentLost })
        {
            var (small, large) = (means[(nameof(ReducerApply.Apply), history, 10_000)], means[(nameof(ReducerApply.Apply), history, 100_000)]);
            output.WriteLine($"apply, {history}: 10k {small / 1_000:F3} us, 100k {large / 1_000:F3} us, ratio {large / small:F2} (rule: <= 3.00)");
            Assert.True(large <= 3 * small, $"Apply cost grew with {history} history: {large:F0} ns at 100k against {small:F0} ns at 10k.");
        }
    }

    private static (string Method, History History, int Size) Case(BenchmarkReport report) => (
        report.BenchmarkCase.Descriptor.WorkloadMethod.Name,
        (History)report.BenchmarkCase.Parameters[nameof(PollBenchmarks.History)],
        (int)report.BenchmarkCase.Parameters[nameof(PollBenchmarks.Size)]);

    [InvocationCount(PollsPerIteration)]
    public class ReducerApply : PollBenchmarks
    {
        private const int PollsPerIteration = 1_000;

        protected override int Polls => PollsPerIteration;

        [Benchmark]
        public ApplyOutcome Apply() => this.ApplyNextPoll();

        [Benchmark]
        public WorkView? ApplyThenFind()
        {
            this.ApplyNextPoll();
            return this.FindHotItem();
        }
    }

    [InvocationCount(PollsPerIteration)]
    public class SourceView : PollBenchmarks
    {
        private const int PollsPerIteration = 8;

        protected override int Polls => PollsPerIteration;

        [Benchmark]
        public IReadOnlyList<WorkView> ApplyThenWork()
        {
            this.ApplyNextPoll();
            return this.WorkOfSource();
        }
    }

    // Each invocation applies the next poll, so IterationSetup builds that iteration's batches outside the timed region.
    [InProcess]
    [MemoryDiagnoser]
    public abstract class PollBenchmarks
    {
        private const int HotItems = 200;
        private const int HistoryChunk = 1_000;
        private const int OneInEveryHundred = 100;

        private readonly WorkKey[] hot = [.. Enumerable.Range(0, HotItems).Select(i => Session($"hot-{i:D4}"))];
        private readonly ProcessIncarnation[] hotProcesses = [.. Enumerable.Range(0, HotItems).Select(i => new ProcessIncarnation(1_000 + i, Start.AddHours(-1)))];
        private ManualClock clock = new(Start);
        private ActivityLedger ledger = new(Settings, new ManualClock(Start));
        private SourceGeneration generation;
        private Dictionary<WorkState, int> historyCensus = [];
        private SafeSourceBatch[] batches = [];
        private WorkState[] expected = [];
        private int next;
        private bool busy;
        private int failures;

        public static ConcurrentQueue<string> Faults { get; } = new();

        [Params(10_000, 100_000)]
        public int Size { get; set; }

        [ParamsAllValues]
        public History History { get; set; }

        protected abstract int Polls { get; }

        [GlobalSetup]
        public void Build()
        {
            this.clock = new ManualClock(Start);
            var settings = this.History == History.OnePercentUnresolved
                ? new LivenessSettings(Settings.MissedPollLimit, Settings.MissedPollMargin, Settings.StartingWindow, Settings.RestartWindow, TimeSpan.FromDays(365))
                : Settings;
            this.ledger = new ActivityLedger(settings, this.clock);
            this.generation = this.ledger.StartGeneration(Claude);
            var history = this.Size - HotItems;
            bool StaysTracked(int index) => this.History != History.EndedOnly && index % OneInEveryHundred == 0;
            for (var offset = 0; offset < history; offset += HistoryChunk)
            {
                var chunk = Enumerable.Range(offset, Math.Min(HistoryChunk, history - offset)).ToArray();
                this.Poll(
                    chunk.Select(i => StaysTracked(i)
                        ? ListedWork.Entry(Session($"old-{i:D6}"), Entrypoint.InteractiveCli)
                        : ListedWork.Entry(Session($"old-{i:D6}"), Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: HistoryProcess(i))),
                    chunk.Where(i => !StaysTracked(i)).Select(HistoryProcess));
            }

            var settled = this.clock.GetUtcNow() + UntilMissedPollLimit(PollEvery) + Settings.LostSettlingWindow;
            while (this.clock.GetUtcNow() <= settled)
            {
                this.Poll([], []);
            }

            var tracked = Enumerable.Range(0, history).Count(StaysTracked);
            var trackedState = this.History == History.OnePercentLost ? WorkState.Lost : WorkState.Stale;
            this.historyCensus = this.HistoryCensus();
            var intended = new Dictionary<WorkState, int> { [WorkState.Ended] = history - tracked };
            if (tracked > 0)
            {
                intended[trackedState] = tracked;
            }

            if (this.ledger.Work(Claude).Count != this.Size || !this.historyCensus.OrderBy(pair => pair.Key).SequenceEqual(intended.OrderBy(pair => pair.Key)))
            {
                throw new InvalidOperationException($"The {this.History} {this.Size} workload built {string.Join(", ", this.historyCensus)}, not {string.Join(", ", intended)}.");
            }
        }

        [IterationSetup]
        public void PreparePolls()
        {
            this.batches = new SafeSourceBatch[this.Polls];
            this.expected = new WorkState[this.Polls];
            this.next = 0;
            var now = this.clock.GetUtcNow();
            for (var i = 0; i < this.Polls; i++)
            {
                this.busy = !this.busy;
                this.expected[i] = this.busy ? WorkState.Busy : WorkState.Idle;
                this.batches[i] = this.HotBatch(now + (PollEvery * (i + 1)), this.busy ? ListedStatus.Busy : ListedStatus.Idle);
            }
        }

        [GlobalCleanup]
        public void CheckWorkload()
        {
            var census = this.HistoryCensus();
            if (this.failures > 0 || !census.OrderBy(pair => pair.Key).SequenceEqual(this.historyCensus.OrderBy(pair => pair.Key)))
            {
                Faults.Enqueue($"{this.GetType().Name} {this.History} {this.Size}: {this.failures} polls not applied or misread; history census " +
                    $"{string.Join(", ", census)} against {string.Join(", ", this.historyCensus)} at build");
            }
        }

        protected ApplyOutcome ApplyNextPoll()
        {
            this.clock.Advance(PollEvery);
            var outcome = this.ledger.Apply(this.batches[this.next++]);
            if (outcome != ApplyOutcome.Applied)
            {
                this.failures++;
            }

            return outcome;
        }

        protected WorkView? FindHotItem()
        {
            var view = this.ledger.Find(this.hot[HotItems - 1]);
            if (view?.State != this.expected[this.next - 1])
            {
                this.failures++;
            }

            return view;
        }

        protected IReadOnlyList<WorkView> WorkOfSource()
        {
            var views = this.ledger.Work(Claude);
            if (views.Count != this.Size)
            {
                this.failures++;
            }

            return views;
        }

        private static ProcessIncarnation HistoryProcess(int index) => new(100_000 + index, Start.AddHours(-2));

        private Dictionary<WorkState, int> HistoryCensus() =>
            this.ledger.Work(Claude).Where(view => view.State is not (WorkState.Busy or WorkState.Idle)).CountBy(view => view.State).ToDictionary();

        private SafeSourceBatch HotBatch(DateTimeOffset at, ListedStatus status) => SafeSourceBatch.Polled(
            Claude,
            this.generation,
            at,
            PollEvery,
            this.hot.Select((key, i) => ListedWork.Entry(key, Entrypoint.Background, status, incarnation: this.hotProcesses[i])),
            this.hotProcesses,
            processInventoryComplete: true);

        private void Poll(IEnumerable<ListedWork> history, IEnumerable<ProcessIncarnation> historyProcesses)
        {
            this.clock.Advance(PollEvery);
            this.ledger.Apply(SafeSourceBatch.Polled(
                Claude,
                this.generation,
                this.clock.GetUtcNow(),
                PollEvery,
                this.hot.Select((key, i) => ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: this.hotProcesses[i])).Concat(history),
                this.hotProcesses.Concat(historyProcesses),
                processInventoryComplete: true));
        }
    }
}

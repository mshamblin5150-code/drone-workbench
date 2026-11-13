namespace Workbench.Core.Tests.Activity.Contract;

using System.Diagnostics;
using Workbench.Core.Activity.Contract;
using Workbench.TestSupport;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

// Opt-in perf probe: dotnet test --filter FullyQualifiedName~ReducerBenchmarks --explicit on. Stopwatch and allocation
// counters only, because BenchmarkDotNet is not an approved dependency. The workload class has no xunit dependency,
// so a BenchmarkDotNet case can wrap Build and ApplyOne unchanged. Absolute budgets belong to issue 24.
public sealed class ReducerBenchmarks(ITestOutputHelper output)
{
    private const int HotItems = 200;
    private const int Warmup = 50;
    private const int AppliesPerRound = 200;
    private const int Rounds = 3;

    [Fact(Explicit = true)]
    public void ApplyCostFollowsTheBatchAndNotTheHistory()
    {
        var workloads = new[] { Workload.Build(10_000), Workload.Build(100_000) };
        foreach (var workload in workloads)
        {
            output.WriteLine($"built {workload.Size} items: {workload.Ended} ended history, {HotItems} live in the hot source");
            workload.Run(Warmup);
        }

        var samples = workloads.ToDictionary(workload => workload.Size, _ => new List<Round>());
        for (var round = 1; round <= Rounds; round++)
        {
            foreach (var workload in workloads)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var result = workload.Run(AppliesPerRound);
                samples[workload.Size].Add(result);
                output.WriteLine(
                    $"round {round} size {workload.Size,7}: apply median {result.ApplyMedianMicroseconds,7:F1} us p95 {result.ApplyP95Microseconds,7:F1} us; " +
                    $"batch-to-projection-ready median {result.ReadyMedianMicroseconds,7:F1} us; allocated {result.BytesPerApply,6:N0} B/apply, " +
                    $"{result.BytesPerReady,6:N0} B/apply+read; " +
                    $"gen0 {result.Gen0Collections}; not applied {result.Failures}");
            }
        }

        var small = Median(samples[10_000].Select(r => r.ApplyMedianMicroseconds));
        var large = Median(samples[100_000].Select(r => r.ApplyMedianMicroseconds));
        output.WriteLine($"median of round medians: 10k {small:F1} us, 100k {large:F1} us, ratio {large / small:F2} (rule: <= 3.00)");
        Assert.All(samples.Values.SelectMany(rounds => rounds), result => Assert.Equal(0, result.Failures));
        Assert.True(large <= 3 * small, $"Apply cost grew with history: {large:F1} us at 100k against {small:F1} us at 10k.");
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private static double Percentile(double[] sorted, double fraction) => sorted[(int)Math.Ceiling(fraction * sorted.Length) - 1];

    private sealed record Round(
        double ApplyMedianMicroseconds, double ApplyP95Microseconds, double ReadyMedianMicroseconds, long BytesPerApply, long BytesPerReady, int Gen0Collections, int Failures);

    // One Claude source holding Size items: HotItems live sessions that every poll lists, and the rest ended history
    // built through the ledger itself, so the history is a real projection rather than injected state.
    private sealed class Workload
    {
        private const int HistoryChunk = 1_000;

        private readonly ManualClock clock = new(Start);
        private readonly ActivityLedger ledger;
        private readonly SourceGeneration generation;
        private readonly WorkKey[] hot = [.. Enumerable.Range(0, HotItems).Select(i => Session($"hot-{i:D4}"))];
        private readonly ProcessIncarnation[] hotProcesses = [.. Enumerable.Range(0, HotItems).Select(i => new ProcessIncarnation(1_000 + i, Start.AddHours(-1)))];
        private bool busy;

        private Workload(int size)
        {
            this.Size = size;
            this.ledger = new ActivityLedger(Settings, this.clock);
            this.generation = this.ledger.StartGeneration(Claude);
        }

        public int Size { get; }

        public int Ended { get; private set; }

        public static Workload Build(int size)
        {
            var workload = new Workload(size);
            var history = size - HotItems;
            for (var offset = 0; offset < history; offset += HistoryChunk)
            {
                var chunk = Enumerable.Range(offset, Math.Min(HistoryChunk, history - offset)).ToArray();
                workload.Poll(
                    chunk.Select(i => ListedWork.Entry(Session($"old-{i:D6}"), Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: HistoryProcess(i))),
                    chunk.Select(HistoryProcess));
            }

            workload.Poll([], []);
            var views = workload.ledger.Work(Claude);
            workload.Ended = views.Count(view => view.State == WorkState.Ended);
            if (views.Count != size || workload.Ended != history)
            {
                throw new InvalidOperationException("The workload did not build the intended projection.");
            }

            return workload;
        }

        // Batches are built before the timed region; only Apply, and Apply plus one read, are timed.
        public Round Run(int applies)
        {
            var batches = new SafeSourceBatch[applies];
            var expected = new WorkState[applies];
            var start = this.clock.GetUtcNow();
            for (var i = 0; i < applies; i++)
            {
                this.busy = !this.busy;
                expected[i] = this.busy ? WorkState.Busy : WorkState.Idle;
                batches[i] = this.HotBatch(start + (Settings.PollInterval * (i + 1)), this.busy ? ListedStatus.Busy : ListedStatus.Idle);
            }

            var apply = new double[applies];
            var ready = new double[applies];
            var bytes = new long[applies];
            var readyBytes = new long[applies];
            var failures = 0;
            var gen0 = GC.CollectionCount(0);
            for (var i = 0; i < applies; i++)
            {
                this.clock.Advance(Settings.PollInterval);
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var began = Stopwatch.GetTimestamp();
                var outcome = this.ledger.Apply(batches[i]);
                var applied = Stopwatch.GetTimestamp();
                bytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                var view = this.ledger.Find(this.hot[HotItems - 1]);
                var projected = Stopwatch.GetTimestamp();
                readyBytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                apply[i] = Stopwatch.GetElapsedTime(began, applied).TotalMicroseconds;
                ready[i] = Stopwatch.GetElapsedTime(began, projected).TotalMicroseconds;
                if (outcome != ApplyOutcome.Applied || view?.State != expected[i])
                {
                    failures++;
                }
            }

            Array.Sort(apply);
            Array.Sort(ready);
            Array.Sort(bytes);
            Array.Sort(readyBytes);
            return new Round(
                Percentile(apply, 0.5),
                Percentile(apply, 0.95),
                Percentile(ready, 0.5),
                bytes[applies / 2],
                readyBytes[applies / 2],
                GC.CollectionCount(0) - gen0,
                failures);
        }

        private static ProcessIncarnation HistoryProcess(int index) => new(100_000 + index, Start.AddHours(-2));

        private SafeSourceBatch HotBatch(DateTimeOffset at, ListedStatus status) => SafeSourceBatch.Polled(
            Claude,
            this.generation,
            at,
            this.hot.Select((key, i) => ListedWork.Entry(key, Entrypoint.Background, status, incarnation: this.hotProcesses[i])),
            this.hotProcesses,
            processInventoryComplete: true);

        private void Poll(IEnumerable<ListedWork> history, IEnumerable<ProcessIncarnation> historyProcesses)
        {
            this.clock.Advance(Settings.PollInterval);
            var batch = SafeSourceBatch.Polled(
                Claude,
                this.generation,
                this.clock.GetUtcNow(),
                this.hot.Select((key, i) => ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: this.hotProcesses[i])).Concat(history),
                this.hotProcesses.Concat(historyProcesses),
                processInventoryComplete: true);
            this.ledger.Apply(batch);
        }
    }
}

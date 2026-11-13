namespace Workbench.Core.Tests.Activity.Contract;

using System.Diagnostics;
using Workbench.Core.Activity.Contract;
using Workbench.TestSupport;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class ReducerBenchmarks(ITestOutputHelper output)
{
    private const int HotItems = 200;
    private const int Warmup = 50;
    private const int AppliesPerRound = 200;
    private const int WholeSourceReadsPerRound = 50;
    private const int Rounds = 5;
    private const int OneInEveryHundredUnresolved = 100;

    [Fact(Explicit = true)]
    public void ApplyCostFollowsTheBatchAndNotTheHistory()
    {
        Workload[] workloads =
        [
            Workload.Build(10_000, unresolvedEvery: null, settles: true),
            Workload.Build(100_000, unresolvedEvery: null, settles: true),
            Workload.Build(10_000, unresolvedEvery: OneInEveryHundredUnresolved, settles: false),
            Workload.Build(100_000, unresolvedEvery: OneInEveryHundredUnresolved, settles: false),
            Workload.Build(10_000, unresolvedEvery: OneInEveryHundredUnresolved, settles: true),
            Workload.Build(100_000, unresolvedEvery: OneInEveryHundredUnresolved, settles: true),
        ];
        foreach (var workload in workloads)
        {
            workload.Run(Warmup, Warmup / 5);
            output.WriteLine($"built {workload.Name}: {workload.Census()} ({HotItems} live in the hot source)");
        }

        var samples = workloads.ToDictionary(workload => workload, _ => new List<Round>());
        for (var round = 1; round <= Rounds; round++)
        {
            foreach (var workload in workloads)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var result = workload.Run(AppliesPerRound, WholeSourceReadsPerRound);
                samples[workload].Add(result);
                output.WriteLine(
                    $"round {round} {workload.Name,-17}: apply median {result.ApplyMedianMicroseconds,7:F1} us p95 {result.ApplyP95Microseconds,7:F1} us; " +
                    $"apply+Find median {result.ReadyMedianMicroseconds,7:F1} us; apply+Work(source) median {result.WholeMedianMicroseconds,9:F1} us; " +
                    $"allocated {result.BytesPerApply,4:N0} B/apply, {result.BytesPerReady,4:N0} B/apply+Find, {result.BytesPerWhole,12:N0} B/apply+Work; " +
                    $"gen0 {result.Gen0Collections}; not applied or wrong {result.Failures}");
            }
        }

        double ApplyMedian(Workload workload) => Median(samples[workload].Select(result => result.ApplyMedianMicroseconds));
        double WholeMedian(Workload workload) => Median(samples[workload].Select(result => result.WholeMedianMicroseconds));
        string Compare(Func<Workload, double> metric, Workload small, Workload large) =>
            $"10k {metric(small):F1} us, 100k {metric(large):F1} us, ratio {metric(large) / metric(small):F2}";

        var (endedSmall, endedLarge, unresolvedSmall, unresolvedLarge, lostSmall, lostLarge) = (workloads[0], workloads[1], workloads[2], workloads[3], workloads[4], workloads[5]);
        output.WriteLine($"apply, Ended-only history: {Compare(ApplyMedian, endedSmall, endedLarge)} (rule: <= 3.00)");
        output.WriteLine(
            $"apply, {unresolvedSmall.Unresolved} vs {unresolvedLarge.Unresolved} unresolved Unknown history items inside the settling window: " +
            $"{Compare(ApplyMedian, unresolvedSmall, unresolvedLarge)} (reported, not gated: every complete poll revisits each tracked item it does not list)");
        output.WriteLine(
            $"apply, {lostSmall.Unresolved} vs {lostLarge.Unresolved} history items settled into Lost: {Compare(ApplyMedian, lostSmall, lostLarge)} (rule: <= 3.00)");
        output.WriteLine(
            $"apply+Work(source), Ended-only history: {Compare(WholeMedian, endedSmall, endedLarge)} " +
            "(Work projects every record of the source, Ended history included, so it scales with total history)");
        output.WriteLine($"apply+Work(source), unresolved history: {Compare(WholeMedian, unresolvedSmall, unresolvedLarge)}");
        output.WriteLine($"apply+Work(source), Lost history: {Compare(WholeMedian, lostSmall, lostLarge)}");
        Assert.All(samples.Values.SelectMany(rounds => rounds), result => Assert.Equal(0, result.Failures));
        Assert.True(
            ApplyMedian(endedLarge) <= 3 * ApplyMedian(endedSmall),
            $"Apply cost grew with Ended history: {ApplyMedian(endedLarge):F1} us at 100k against {ApplyMedian(endedSmall):F1} us at 10k.");
        Assert.True(
            ApplyMedian(lostLarge) <= 3 * ApplyMedian(lostSmall),
            $"Apply cost grew with Lost history: {ApplyMedian(lostLarge):F1} us at 100k against {ApplyMedian(lostSmall):F1} us at 10k.");
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private static double Percentile(double[] sorted, double fraction) => sorted[(int)Math.Ceiling(fraction * sorted.Length) - 1];

    private sealed record Round(
        double ApplyMedianMicroseconds,
        double ApplyP95Microseconds,
        double ReadyMedianMicroseconds,
        double WholeMedianMicroseconds,
        long BytesPerApply,
        long BytesPerReady,
        long BytesPerWhole,
        int Gen0Collections,
        int Failures);

    private sealed class Workload
    {
        private const int HistoryChunk = 1_000;

        private readonly ManualClock clock = new(Start);
        private readonly ActivityLedger ledger;
        private readonly SourceGeneration generation;
        private readonly WorkKey[] hot = [.. Enumerable.Range(0, HotItems).Select(i => Session($"hot-{i:D4}"))];
        private readonly ProcessIncarnation[] hotProcesses = [.. Enumerable.Range(0, HotItems).Select(i => new ProcessIncarnation(1_000 + i, Start.AddHours(-1)))];
        private bool busy;

        private Workload(int size, int? unresolvedEvery, bool settles)
        {
            this.Size = size;
            this.Name = $"{(unresolvedEvery is { } every ? $"{100.0 / every:0.#}% {(settles ? "Lost" : "unresolved")}" : "Ended-only")} {size / 1_000}k";
            var settings = settles
                ? Settings
                : new LivenessSettings(Settings.MissedPollLimit, Settings.MissedPollMargin, Settings.StartingWindow, Settings.RestartWindow, TimeSpan.FromDays(365));
            this.ledger = new ActivityLedger(settings, this.clock);
            this.generation = this.ledger.StartGeneration(Claude);
        }

        public int Size { get; }

        public string Name { get; }

        public int Unresolved { get; private set; }

        public static Workload Build(int size, int? unresolvedEvery, bool settles)
        {
            var workload = new Workload(size, unresolvedEvery, settles);
            var history = size - HotItems;
            bool StaysTracked(int index) => unresolvedEvery is { } every && index % every == 0;
            for (var offset = 0; offset < history; offset += HistoryChunk)
            {
                var chunk = Enumerable.Range(offset, Math.Min(HistoryChunk, history - offset)).ToArray();
                workload.Poll(
                    chunk.Select(i => StaysTracked(i)
                        ? ListedWork.Entry(Session($"old-{i:D6}"), Entrypoint.InteractiveCli)
                        : ListedWork.Entry(Session($"old-{i:D6}"), Entrypoint.InteractiveCli, ListedStatus.Idle, incarnation: HistoryProcess(i))),
                    chunk.Where(i => !StaysTracked(i)).Select(HistoryProcess));
            }

            workload.Poll([], []);
            var views = workload.ledger.Work(Claude);
            var expectedTracked = Enumerable.Range(0, history).Count(StaysTracked);
            var ended = views.Count(view => view.State == WorkState.Ended);
            workload.Unresolved = views.Count - ended - HotItems;
            if (views.Count != size || workload.Unresolved != expectedTracked || ended != history - expectedTracked)
            {
                throw new InvalidOperationException("The workload did not build the intended projection.");
            }

            return workload;
        }

        public string Census() =>
            string.Join(", ", this.ledger.Work(Claude).CountBy(view => view.State).OrderBy(pair => pair.Key).Select(pair => $"{pair.Value} {pair.Key}"));

        public Round Run(int applies, int wholeSourceReads)
        {
            var total = applies + wholeSourceReads;
            var batches = new SafeSourceBatch[total];
            var expected = new WorkState[total];
            var start = this.clock.GetUtcNow();
            for (var i = 0; i < total; i++)
            {
                this.busy = !this.busy;
                expected[i] = this.busy ? WorkState.Busy : WorkState.Idle;
                batches[i] = this.HotBatch(start + (PollEvery * (i + 1)), this.busy ? ListedStatus.Busy : ListedStatus.Idle);
            }

            var apply = new double[applies];
            var ready = new double[applies];
            var bytes = new long[applies];
            var readyBytes = new long[applies];
            var whole = new double[wholeSourceReads];
            var wholeBytes = new long[wholeSourceReads];
            var failures = 0;
            var gen0 = GC.CollectionCount(0);
            for (var i = 0; i < applies; i++)
            {
                this.clock.Advance(PollEvery);
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

            for (var i = 0; i < wholeSourceReads; i++)
            {
                this.clock.Advance(PollEvery);
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var began = Stopwatch.GetTimestamp();
                var outcome = this.ledger.Apply(batches[applies + i]);
                var views = this.ledger.Work(Claude);
                var projected = Stopwatch.GetTimestamp();
                wholeBytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                whole[i] = Stopwatch.GetElapsedTime(began, projected).TotalMicroseconds;
                if (outcome != ApplyOutcome.Applied || views.Count != this.Size || this.ledger.Find(this.hot[HotItems - 1])?.State != expected[applies + i])
                {
                    failures++;
                }
            }

            Array.Sort(apply);
            Array.Sort(ready);
            Array.Sort(whole);
            Array.Sort(bytes);
            Array.Sort(readyBytes);
            Array.Sort(wholeBytes);
            return new Round(
                Percentile(apply, 0.5),
                Percentile(apply, 0.95),
                Percentile(ready, 0.5),
                Percentile(whole, 0.5),
                bytes[applies / 2],
                readyBytes[applies / 2],
                wholeBytes[wholeSourceReads / 2],
                GC.CollectionCount(0) - gen0,
                failures);
        }

        private static ProcessIncarnation HistoryProcess(int index) => new(100_000 + index, Start.AddHours(-2));

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
            var batch = SafeSourceBatch.Polled(
                Claude,
                this.generation,
                this.clock.GetUtcNow(),
                PollEvery,
                this.hot.Select((key, i) => ListedWork.Entry(key, Entrypoint.Background, ListedStatus.Idle, incarnation: this.hotProcesses[i])).Concat(history),
                this.hotProcesses.Concat(historyProcesses),
                processInventoryComplete: true);
            this.ledger.Apply(batch);
        }
    }
}

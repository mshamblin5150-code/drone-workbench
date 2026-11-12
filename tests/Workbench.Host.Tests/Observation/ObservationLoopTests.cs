namespace Workbench.Host.Tests.Observation;

using System.Collections.Concurrent;
using Workbench.Host.Observation;
using Workbench.Host.Sources.Claude;
using Xunit;
using static Workbench.Host.Tests.Observation.SyntheticClaude;

public sealed class ObservationLoopTests
{
    private static readonly ObservationSettings Settings = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));

    [Fact]
    public async Task FirstCaptureRunsBeforeAnyWait()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var events = new ConcurrentQueue<string>();
        var firstWait = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ObservationStore();
        using var loop = new ObservationLoop(
            Source(new RecordingReader(events)),
            store,
            Settings,
            (interval, token) =>
            {
                events.Enqueue("wait");
                firstWait.TrySetResult(interval);
                return Task.Delay(Timeout.Infinite, token);
            });
        await loop.StartAsync(cancellationToken);
        Assert.Equal(Settings.PollInterval, await firstWait.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
        Assert.Equal(1, store.Current.Revision);
        Assert.Equal(ClaudeObservation.CaptureState.Observed, store.Current.LatestAttempt!.State);
        Assert.Equal(new[] { "version", "agents", "wait" }, events);
        await loop.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task CapturesNeverOverlapAndEachWaitFollowsACompletedCapture()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var events = new ConcurrentQueue<string>();
        var reader = new RecordingReader(events);
        var waits = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ObservationStore();
        using var loop = new ObservationLoop(
            Source(reader),
            store,
            Settings,
            async (_, token) =>
            {
                events.Enqueue("wait");
                if (++waits == 5)
                {
                    done.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }

                await Task.Yield();
            });
        await loop.StartAsync(cancellationToken);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        await loop.StopAsync(cancellationToken);
        Assert.Equal(1, reader.MaxConcurrent);
        Assert.Equal(5, store.Current.Revision);
        Assert.Equal(Enumerable.Repeat(new[] { "version", "agents", "wait" }, 5).SelectMany(cycle => cycle), events);
    }

    [Fact]
    public async Task StoppingDuringACaptureDoesNotPublishTheCancelledResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reader = new BlockingReader();
        var store = new ObservationStore();
        using var loop = new ObservationLoop(Source(reader), store, Settings, (_, token) => Task.Delay(Timeout.Infinite, token));
        await loop.StartAsync(cancellationToken);
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        await loop.StopAsync(cancellationToken);
        Assert.True(reader.Cancelled);
        Assert.Equal(0, store.Current.Revision);
        Assert.Null(store.Current.LatestAttempt);
    }

    [Fact]
    public async Task EachAttemptIsPublishedAndAFailureKeepsTheStaleObservation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var states = new ConcurrentQueue<ObservationState>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new ObservationStore();
        using var loop = new ObservationLoop(
            Source(new ScriptedReader(false, true, false)),
            store,
            Settings,
            (_, token) =>
            {
                states.Enqueue(store.Current);
                if (states.Count < 3)
                {
                    return Task.CompletedTask;
                }

                done.TrySetResult();
                return Task.Delay(Timeout.Infinite, token);
            });
        await loop.StartAsync(cancellationToken);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        await loop.StopAsync(cancellationToken);
        var published = states.ToArray();
        Assert.Equal(new long[] { 1, 2, 3 }, published.Select(state => state.Revision));
        Assert.Equal(new[] { 1, 0, 1 }, published.Select(state => state.ConsecutiveFailures));
        Assert.Null(published[0].LatestObservation);
        Assert.Equal(ClaudeObservation.FailureReason.Timeout, published[0].LatestAttempt!.Failure);
        var observation = published[1].LatestObservation;
        Assert.NotNull(observation);
        Assert.Same(observation, published[1].LatestAttempt);
        Assert.Same(observation, published[2].LatestObservation);
        Assert.Equal(ClaudeObservation.FailureReason.Timeout, published[2].LatestAttempt!.Failure);
        Assert.False(ObservationState.Succeeded(published[2].LatestAttempt!));
    }

    private sealed class ScriptedReader(params bool[] succeeds) : IClaudeCommandReader
    {
        private int captures;

        public Task<ClaudeCommandResult> ReadAsync(ClaudeReadOperation operation, CancellationToken cancellationToken)
        {
            if (operation == ClaudeReadOperation.Version)
            {
                this.captures++;
            }

            return Task.FromResult(succeeds[this.captures - 1]
                ? Reader.Result(operation, "[]")
                : new ClaudeCommandResult(ClaudeObservation.FailureReason.Timeout, ClaudeObservation.RootScope.DefaultRoot));
        }
    }

    private sealed class RecordingReader(ConcurrentQueue<string> events) : IClaudeCommandReader
    {
        private int active;
        private int maxConcurrent;

        internal int MaxConcurrent => Volatile.Read(ref this.maxConcurrent);

        public async Task<ClaudeCommandResult> ReadAsync(ClaudeReadOperation operation, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref this.active);
            InterlockedMax(ref this.maxConcurrent, now);
            events.Enqueue(operation == ClaudeReadOperation.Version ? "version" : "agents");
            await Task.Yield();
            Interlocked.Decrement(ref this.active);
            return Reader.Result(operation, "[]");
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value &&
                Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    private sealed class BlockingReader : IClaudeCommandReader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Cancelled { get; private set; }

        public async Task<ClaudeCommandResult> ReadAsync(ClaudeReadOperation operation, CancellationToken cancellationToken)
        {
            this.Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                this.Cancelled = true;
                throw;
            }

            return Reader.Result(operation, "[]");
        }
    }
}

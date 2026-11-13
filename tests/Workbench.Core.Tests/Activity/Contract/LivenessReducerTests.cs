namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Xunit;

public sealed class LivenessReducerTests(ITestOutputHelper output)
{
    private static readonly Signal[] Unlisted =
        [Signal.UnlistedAlive, Signal.UnlistedExited, Signal.UnlistedNoProcess, Signal.UnlistedLivenessUnknown, Signal.UnlistedAfterStop];

    [Fact]
    public void TablePrintsOneLinePerAllowedTransitionAndDecidesEveryStateAndSignal()
    {
        foreach (var transition in Liveness.Transitions)
        {
            output.WriteLine(transition.ToString());
        }

        Assert.NotEmpty(Liveness.Transitions);
        Assert.Equal(Liveness.Transitions.Length, Liveness.Transitions.DistinctBy(t => (t.From, t.Signal)).Count());
        foreach (var from in Liveness.Domain)
        {
            foreach (var signal in Enum.GetValues<Signal>())
            {
                Assert.True(Liveness.IsDecided(from, signal), $"{signal} from {from?.ToString() ?? "new work"} has no rule.");
            }
        }
    }

    [Fact]
    public void NoTransitionEntersAStateFromWhatTheContractSaysNeverEntersIt()
    {
        var moves = Liveness.Transitions;

        Assert.DoesNotContain(moves, t => t.To == WorkState.Starting && t.From is WorkState.Busy or WorkState.Waiting or WorkState.Idle or WorkState.Restarting);
        Assert.All(moves.Where(t => t.To == WorkState.Busy), t => Assert.Equal(Signal.ListedBusy, t.Signal));
        Assert.All(moves.Where(t => t.To == WorkState.Idle), t => Assert.Equal(Signal.ListedIdle, t.Signal));
        Assert.All(moves.Where(t => t.To == WorkState.Waiting), t => Assert.Contains(t.Signal, new[] { Signal.ListedWaiting, Signal.HookWaiting }));
        Assert.All(moves.Where(t => t.To == WorkState.Stopped), t => Assert.Equal(Signal.ListedStopped, t.Signal));
        Assert.All(moves.Where(t => t.To == WorkState.NotLoaded), t => Assert.Equal(Signal.ListedNotLoaded, t.Signal));
        Assert.DoesNotContain(moves, t => t.To == WorkState.Restarting && Unlisted.Contains(t.Signal));
        Assert.All(
            moves.Where(t => t.To == WorkState.Ended),
            t => Assert.Contains(t.Signal, new[] { Signal.UnlistedExited, Signal.SessionEndThenExit, Signal.UnlistedAfterStop }));
        Assert.All(moves.Where(t => t.Signal == Signal.HookWaiting), t => Assert.Equal(WorkState.Waiting, t.To));
        Assert.DoesNotContain(moves, t => t.From == WorkState.Ended && t.Signal is Signal.SourceDisconnected or Signal.ConfirmationExpired);
    }
}

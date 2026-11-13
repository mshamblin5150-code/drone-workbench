namespace Workbench.Host.Tests.Observation;

using Workbench.Host.Observation;
using Xunit;
using static Workbench.Host.Tests.Observation.SyntheticClaude;

public sealed class ObservationStoreTests
{
    [Fact]
    public async Task ConcurrentPublishesLoseNoRevision()
    {
        const int Writers = 8;
        const int PublishesPerWriter = 2000;
        var store = new ObservationStore();
        using var start = new Barrier(Writers);
        var writers = Enumerable.Range(0, Writers).Select(writer => Task.Run(() =>
        {
            start.SignalAndWait();
            for (var index = 0; index < PublishesPerWriter; index++)
            {
                store.Publish(writer % 2 == 0 ? PartialSuccess(Start) : Failure(Start));
            }
        }));
        await Task.WhenAll(writers).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(Writers * PublishesPerWriter, store.Current.Revision);
    }
}

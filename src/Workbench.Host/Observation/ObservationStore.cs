namespace Workbench.Host.Observation;

using Workbench.Host.Sources.Claude;

internal sealed class ObservationStore
{
    private ObservationState current = ObservationState.Empty;

    internal ObservationState Current => Volatile.Read(ref this.current);

    internal void Publish(ClaudeObservation attempt)
    {
        var observed = this.Current;
        while (true)
        {
            var replaced = Interlocked.CompareExchange(ref this.current, observed.With(attempt), observed);
            if (ReferenceEquals(replaced, observed))
            {
                return;
            }

            observed = replaced;
        }
    }
}

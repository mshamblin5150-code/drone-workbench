namespace Workbench.TestSupport;

public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset now;

    public ManualClock(DateTimeOffset start)
    {
        if (start.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The start time must be UTC.", nameof(start));
        }

        this.now = start;
    }

    public override DateTimeOffset GetUtcNow() => this.now;

    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        this.now += by;
    }
}

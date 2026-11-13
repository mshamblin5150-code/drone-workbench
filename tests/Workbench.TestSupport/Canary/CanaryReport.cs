namespace Workbench.TestSupport.Canary;

public sealed record CanaryReport(int FixtureMatches, int RealValueMatches, IReadOnlyList<string> MatchedInputs)
{
    public bool Clean => this.FixtureMatches == 0 && this.RealValueMatches == 0;
}

namespace Workbench.Core.Tests.Activity.Contract;

using System.Text.Json;
using Workbench.TestSupport.Canary;
using Xunit;

public sealed class CanaryScannerTests : IDisposable
{
    private const string Canary = "CANARY-7Q4M2X9B";
    private const string SyntheticUser = "fixture-person";
    private const string SyntheticProfile = @"C:\Users\" + SyntheticUser;
    private const string SyntheticPid = "4321";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "workbench-canary-" + Guid.NewGuid().ToString("N"));

    public CanaryScannerTests() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, recursive: true);

    [Fact]
    public void FindsPlantedFixtureCanariesInAFileLogTextCapturedOutputAndAStagedDiff()
    {
        var file = this.Plant("fixture.json", $$"""{"name":"{{Canary}}"}""");
        var scanner = new CanaryScanner([Canary], () => []);

        var report = scanner.Scan(
        [
            CanaryInput.File(file),
            CanaryInput.Captured("host log", $"warn: parse failed near {Canary}"),
            CanaryInput.Captured("cli stdout", $"{Canary}\n"),
            CanaryInput.StagedDiff($"+++ b/src/x.cs\n+var name = \"{Canary}\";\n-var old = \"{Canary}\";\n"),
            CanaryInput.Captured("clean sink", "nothing to see"),
        ]);

        Assert.Equal(4, report.FixtureMatches);
        Assert.Equal(0, report.RealValueMatches);
        Assert.Equal(new[] { "fixture.json", "host log", "cli stdout", "staged diff" }, report.MatchedInputs);
        Assert.False(report.Clean);
    }

    [Fact]
    public void ReadsRealValuesAtScanTimeAndFindsEscapedAndForwardSlashPathForms()
    {
        var reads = 0;
        var scanner = new CanaryScanner([], () =>
        {
            reads++;
            return [SyntheticProfile];
        });
        Assert.Equal(0, reads);

        var report = scanner.Scan(
        [
            CanaryInput.Captured("raw", $"cwd={SyntheticProfile}\\repo"),
            CanaryInput.Captured("json", JsonSerializer.Serialize(new { cwd = SyntheticProfile })),
            CanaryInput.Captured("slashes", "cwd=c:/users/FIXTURE-PERSON/repo"),
        ]);

        Assert.Equal(1, reads);
        Assert.Equal(3, report.RealValueMatches);
        Assert.Equal(new[] { "raw", "json", "slashes" }, report.MatchedInputs);
    }

    [Fact]
    public void MatchesProcessIdsOnlyOnDigitBoundaries()
    {
        var scanner = new CanaryScanner([], () => [SyntheticPid]);

        var report = scanner.Scan([CanaryInput.Captured("log", "pid=4321; port 143210; 4321x; 43210")]);

        Assert.Equal(2, report.RealValueMatches);
    }

    [Fact]
    public void ReportsCountsAndFileNamesButNeverAValueOrADirectory()
    {
        var file = this.Plant("leak.txt", $"{Canary} {SyntheticProfile} {SyntheticPid}");
        var scanner = new CanaryScanner([Canary], () => [SyntheticProfile, SyntheticPid]);

        var report = scanner.Scan([CanaryInput.File(file)]);

        Assert.Equal(1, report.FixtureMatches);
        Assert.Equal(2, report.RealValueMatches);
        var rendered = report + JsonSerializer.Serialize(report);
        Assert.Contains("leak.txt", rendered, StringComparison.Ordinal);
        Assert.True(new CanaryScanner([Canary, this.directory], () => [SyntheticProfile, SyntheticPid])
            .Scan([CanaryInput.Captured("report", rendered)]).Clean);
    }

    [Fact]
    public void TheMachineValueReaderReturnsValuesTheScannerFindsWhenOneIsPlanted()
    {
        var values = CanaryScanner.ReadMachineValues().ToArray();
        Assert.True(values.Length > 0, "The reader returned no machine values.");
        Assert.True(values.All(value => !string.IsNullOrEmpty(value)), "The reader returned an empty machine value.");
        var planted = values.First(value => value.Length >= CanaryScanner.MinimumValueLength);

        var report = new CanaryScanner([], CanaryScanner.ReadMachineValues).Scan(
        [
            CanaryInput.Captured("planted", $"cwd={planted}"),
            CanaryInput.Captured("synthetic", "cwd=synthetic-project"),
        ]);

        Assert.True(report.RealValueMatches > 0, "A planted machine value was not found.");
        Assert.Equal(new[] { "planted" }, report.MatchedInputs);
    }

    [Fact]
    public void AMatchedInputWhoseNameHoldsARealValueIsReportedByARedactedName()
    {
        var file = this.Plant($"{SyntheticUser}-notes.txt", $"home={SyntheticProfile}");
        var scanner = new CanaryScanner([], () => [SyntheticProfile, SyntheticUser]);

        var report = scanner.Scan([CanaryInput.File(file)]);

        Assert.Equal(new[] { "[redacted]-notes.txt" }, report.MatchedInputs);
        Assert.True(scanner.Scan([CanaryInput.Captured("report", report + JsonSerializer.Serialize(report))]).Clean);
    }

    [Fact]
    public void AnUnreadableFileIsACountedErrorAndNeverAMessageWithItsPath()
    {
        var missing = CanaryInput.File(Path.Combine(this.directory, "missing.txt"));

        var report = new CanaryScanner([Canary], () => []).Scan([missing, CanaryInput.Captured("log", Canary)]);

        Assert.Equal((1, 1), (report.ReadErrors, report.FixtureMatches));
        Assert.False(new CanaryScanner([], () => []).Scan([missing]).Clean);
        Assert.True(new CanaryScanner([this.directory], () => []).Scan([CanaryInput.Captured("report", report + JsonSerializer.Serialize(report))]).Clean);
    }

    [Fact]
    public void ValuesTooShortToScanAreCountedNotDroppedSilently()
    {
        var scanner = new CanaryScanner(["ab"], () => [string.Empty, "xy", SyntheticProfile]);

        var report = scanner.Scan([CanaryInput.Captured("log", $"ab xy {SyntheticProfile}")]);

        Assert.Equal(3, report.SkippedShortValues);
        Assert.Equal((0, 1), (report.FixtureMatches, report.RealValueMatches));
    }

    [Fact]
    public void AStagedDiffScansAddedLinesThatStartWithPlusSignsAndNewPaths()
    {
        var diff = string.Join(
            '\n',
            "diff --git a/src/x.cs b/src/x.cs",
            "--- a/src/x.cs",
            $"+++ b/src/{Canary}.cs",
            "@@ -1,2 +1,2 @@",
            $"-var old = \"{Canary}\";",
            $"+++counter; // {Canary}",
            " unchanged();");

        var report = new CanaryScanner([Canary], () => []).Scan([CanaryInput.StagedDiff(diff)]);

        Assert.Equal(2, report.FixtureMatches);
    }

    [Fact]
    public void ScanningWritesNothing()
    {
        var file = this.Plant("fixture.txt", Canary);
        var before = Snapshot(this.directory);

        new CanaryScanner([Canary], () => [SyntheticProfile]).Scan([CanaryInput.File(file)]);

        Assert.Equal(before, Snapshot(this.directory));
    }

    private static string[] Snapshot(string root) =>
    [
        .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => $"{Path.GetFileName(path)}:{new FileInfo(path).Length}:{File.GetLastWriteTimeUtc(path):O}")
            .Order(StringComparer.Ordinal),
    ];

    private string Plant(string name, string text)
    {
        var path = Path.Combine(this.directory, name);
        File.WriteAllText(path, text);
        return path;
    }
}

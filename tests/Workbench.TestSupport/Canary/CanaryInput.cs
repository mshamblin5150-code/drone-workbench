namespace Workbench.TestSupport.Canary;

public sealed class CanaryInput
{
    private readonly Func<string> read;

    private CanaryInput(string name, Func<string> read)
    {
        this.Name = name;
        this.read = read;
    }

    public string Name { get; }

    public static CanaryInput File(string path) => new(Path.GetFileName(path), () => System.IO.File.ReadAllText(path));

    public static CanaryInput Captured(string name, string text) => new(name, () => text);

    public static CanaryInput StagedDiff(string unifiedDiff) => new("staged diff", () => AddedLines(unifiedDiff));

    internal string Read() => this.read();

    private static string AddedLines(string unifiedDiff) => string.Join(
        '\n',
        unifiedDiff.Split('\n').Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal)));
}

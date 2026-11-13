namespace Workbench.TestSupport.Canary;

// Real values are read inside each Scan call and live only in its locals, so a scanner never holds or writes one.
public sealed class CanaryScanner(IReadOnlyCollection<string> fixtureCanaries, Func<IEnumerable<string>> readRealValues)
{
    // Shorter values would match ordinary text and turn every report into noise.
    public const int MinimumValueLength = 3;

    public static IEnumerable<string> ReadMachineValues()
    {
        yield return Environment.UserName;
        yield return Environment.MachineName;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.TrimEndingDirectorySeparator(Path.GetTempPath());
    }

    public CanaryReport Scan(IEnumerable<CanaryInput> inputs)
    {
        var fixtures = Variants(fixtureCanaries);
        var real = Variants(readRealValues());
        var fixtureMatches = 0;
        var realMatches = 0;
        var matchedInputs = new List<string>();
        foreach (var input in inputs)
        {
            var text = input.Read();
            var inputFixtureMatches = fixtures.Sum(value => Count(text, value));
            var inputRealMatches = real.Sum(value => Count(text, value));
            if (inputFixtureMatches + inputRealMatches > 0)
            {
                matchedInputs.Add(input.Name);
            }

            fixtureMatches += inputFixtureMatches;
            realMatches += inputRealMatches;
        }

        return new CanaryReport(fixtureMatches, realMatches, matchedInputs);
    }

    // A path also leaks as forward slashes or as a JSON-escaped string.
    private static string[] Variants(IEnumerable<string> values) =>
    [
        .. values
            .Where(value => value.Length >= MinimumValueLength)
            .SelectMany(value => value.Contains('\\', StringComparison.Ordinal)
                ? new[] { value, value.Replace('\\', '/'), value.Replace("\\", "\\\\", StringComparison.Ordinal) }
                : [value])
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    // A number such as a process id counts only at digit boundaries, so 4321 is not found inside 143210.
    private static int Count(string text, string value)
    {
        var numeric = value.All(char.IsAsciiDigit);
        var count = 0;
        var at = text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            var end = at + value.Length;
            if (!numeric || (!IsDigitAt(text, at - 1) && !IsDigitAt(text, end)))
            {
                count++;
                at = text.IndexOf(value, end, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                at = text.IndexOf(value, at + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        return count;
    }

    private static bool IsDigitAt(string text, int index) => index >= 0 && index < text.Length && char.IsAsciiDigit(text[index]);
}

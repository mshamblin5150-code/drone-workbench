namespace Workbench.Cli.Tests;

using System.Text.Json;
using Workbench.Tests;
using Xunit;

public sealed class SourcesCommandTests
{
    [Fact]
    public async Task MissingHostIsReportedAsUnreachableNotAsAnEmptySnapshot()
    {
        using var process = TestProcess.Start("Cli", "sources", "--json");
        var result = await process.CompleteAsync();
        Assert.Equal(3, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Assert.False(json.RootElement.GetProperty("hostReachable").GetBoolean());
        Assert.Equal("NoHostListening", json.RootElement.GetProperty("reason").GetString());
        Assert.False(json.RootElement.TryGetProperty("sources", out _));
    }

    [Fact]
    public async Task SourcesWithoutJsonIsAUsageError()
    {
        using var process = TestProcess.Start("Cli", "sources");
        var result = await process.CompleteAsync();
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("--json", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidInstanceSuffixIsRefused()
    {
        using var process = TestProcess.Start(
            "Cli",
            new Dictionary<string, string> { ["DRONE_WORKBENCH_INSTANCE"] = "Not-Valid" },
            "sources",
            "--json");
        var result = await process.CompleteAsync();
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("DRONE_WORKBENCH_INSTANCE", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourcesReadsTheRunningHostSnapshotAndASecondHostCannotStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var environment = new Dictionary<string, string> { ["DRONE_WORKBENCH_INSTANCE"] = TestProcess.NewInstance() };
        using var host = TestProcess.Start("Host", environment);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await host.ReadUntilStartedAsync(deadline.Token);

        using var snapshot = await ReadPublishedSnapshotAsync(environment, deadline.Token);
        var claude = Assert.Single(snapshot.RootElement.GetProperty("sources").EnumerateArray().ToList());
        Assert.Equal("claude", claude.GetProperty("source").GetString());
        Assert.Equal("None", claude.GetProperty("freshness").GetString());
        Assert.Equal("Unavailable", claude.GetProperty("lastAttempt").GetProperty("state").GetString());
        Assert.Equal("MissingExecutable", claude.GetProperty("lastAttempt").GetProperty("failure").GetString());
        Assert.Equal(JsonValueKind.Null, claude.GetProperty("observation").ValueKind);
        Assert.Equal("Unknown", claude.GetProperty("supervisorHealth").GetString());
        Assert.False(claude.GetProperty("controlsAvailable").GetBoolean());
        Assert.NotEmpty(claude.GetProperty("coverageGaps").EnumerateArray());

        using (var duplicate = TestProcess.Start("Host", environment))
        {
            var result = await duplicate.CompleteAsync();
            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(result.StandardOutput);
            Assert.Single(result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        using var stillServing = await ReadPublishedSnapshotAsync(environment, deadline.Token);
        Assert.Equal(1, stillServing.RootElement.GetProperty("schema").GetInt32());
    }

    private static async Task<JsonDocument> ReadPublishedSnapshotAsync(
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var cli = TestProcess.Start("Cli", environment, "sources", "--json");
            var result = await cli.CompleteAsync();
            Assert.Equal(0, result.ExitCode);
            var snapshot = JsonDocument.Parse(result.StandardOutput);
            if (snapshot.RootElement.GetProperty("revision").GetInt64() > 0)
            {
                return snapshot;
            }

            snapshot.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}

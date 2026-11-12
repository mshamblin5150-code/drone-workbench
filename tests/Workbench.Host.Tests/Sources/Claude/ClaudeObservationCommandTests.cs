namespace Workbench.Host.Tests.Sources.Claude;

using System.Diagnostics;
using System.Text.Json;
using Xunit;

public sealed class ClaudeObservationCommandTests
{
    [Fact]
    public async Task MissingCliWritesOnlyFailureJsonAndStartsNoListener()
    {
        var result = await RunAsync(["observe-claude", "--json"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("Unavailable", document.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("sessions").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("observedAt").ValueKind);
        Assert.Equal("Unknown", document.RootElement.GetProperty("supervisorHealth").GetString());
        Assert.False(document.RootElement.GetProperty("controlsAvailable").GetBoolean());
        Assert.DoesNotContain("Now listening", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("daemon")]
    [InlineData("rm")]
    [InlineData("--targets")]
    [InlineData("observe-claude")]
    public async Task UnsupportedVerbsAndIncompleteCommandsDispatchNothing(string argument)
    {
        var result = await RunAsync([argument]);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Usage:", result.Error, StringComparison.Ordinal);
    }

    private static async Task<CommandResult> RunAsync(string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Workbench.Host.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["PATH"] = string.Empty;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}

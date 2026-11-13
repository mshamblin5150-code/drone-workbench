namespace Workbench.Cli.Tests;

using System.Text.Json;
using Workbench.Tests;
using Xunit;

public sealed class CommandTests
{
    [Fact]
    public async Task DiagnosticsAreMachineReadableAndDeclareAvailableFeatures()
    {
        using var process = TestProcess.Start("Cli", "diagnostics", "--json");
        var result = await process.CompleteAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("foundation", json.RootElement.GetProperty("stage").GetString());
        Assert.True(json.RootElement.GetProperty("hostCommunicationAvailable").GetBoolean());
        Assert.False(json.RootElement.GetProperty("collectionAvailable").GetBoolean());
        Assert.False(json.RootElement.GetProperty("controlsAvailable").GetBoolean());
    }

    [Theory]
    [InlineData("--help", "diagnostics")]
    [InlineData("--version", "1.0.0")]
    public async Task StandardDiscoveryCommandsSucceed(string argument, string expected)
    {
        using var process = TestProcess.Start("Cli", argument);
        var result = await process.CompleteAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expected, result.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cleanup")]
    [InlineData("--unknown")]
    public async Task UnknownCommandsFailWithoutClaimingSuccess(string argument)
    {
        using var process = TestProcess.Start("Cli", argument);
        var result = await process.CompleteAsync();
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.StandardError);
    }
}

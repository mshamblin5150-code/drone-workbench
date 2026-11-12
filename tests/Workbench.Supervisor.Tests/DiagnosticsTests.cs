namespace Workbench.Supervisor.Tests;

using System.Text.Json;
using Workbench.Tests;
using Xunit;

public sealed class DiagnosticsTests
{
    [Fact]
    public async Task DiagnosticsExitWithoutClaimingBackgroundSupervision()
    {
        using var process = TestProcess.Start("Supervisor", "diagnostics");
        var result = await process.CompleteAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.False(json.RootElement.GetProperty("supervisionAvailable").GetBoolean());
        Assert.False(json.RootElement.GetProperty("startupRegistrationAvailable").GetBoolean());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("start", null)]
    [InlineData("diagnostics", "extra")]
    public async Task UnsupportedInvocationsReturnUsageAndFailure(string? first, string? second)
    {
        string[] arguments = first is null ? [] : second is null ? [first] : [first, second];
        using var process = TestProcess.Start("Supervisor", arguments);
        var result = await process.CompleteAsync();
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("Usage:", result.StandardError, StringComparison.Ordinal);
    }
}

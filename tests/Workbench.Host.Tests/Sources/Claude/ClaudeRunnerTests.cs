namespace Workbench.Host.Tests.Sources.Claude;

using System.Text;
using System.Text.Json;
using Workbench.Host.Sources.Claude;
using Xunit;

public sealed class ClaudeRunnerTests
{
    [Fact]
    public void CommandRegistryContainsOnlyReadOperations()
    {
        Assert.Equal(2, Enum.GetValues<ClaudeReadOperation>().Length);
        Assert.Equal(["--version"], ClaudeReadCommandRunner.ArgumentsFor(ClaudeReadOperation.Version));
        Assert.Equal(["agents", "--json", "--all"], ClaudeReadCommandRunner.ArgumentsFor(ClaudeReadOperation.AgentView));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaudeReadCommandRunner.ArgumentsFor((ClaudeReadOperation)123));
    }

    [Fact]
    public async Task UndefinedOperationRefusesBeforeAnyProcessCanStart()
    {
        var runner = new ClaudeReadCommandRunner(null, null, TimeSpan.FromSeconds(1), 1024);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.ReadAsync((ClaudeReadOperation)123, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EmptyAndRelativePathEntriesCannotSelectTheCurrentDirectory()
    {
        using var fixture = new RootFixture();
        Assert.Null(ClaudeReadCommandRunner.FindExecutable($";.;relative;{Path.GetFileName(fixture.Directory.FullName)}"));
        Assert.Equal(fixture.Executable, ClaudeReadCommandRunner.FindExecutable($"\"{fixture.Directory.FullName}\""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DefaultRootIsAppliedOnlyInTheChild(string? root)
    {
        using var fixture = new RootFixture();
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var start = ClaudeReadCommandRunner.CreateStartInfo(
            fixture.Executable, root, ClaudeReadOperation.AgentView, fixture.Directory.FullName);
        Assert.NotNull(start);
        Assert.False(start.Environment.ContainsKey("CLAUDE_CONFIG_DIR"));
        Assert.Equal(fixture.Directory.FullName, start.WorkingDirectory);
        Assert.Equal(previous, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));
    }

    [Fact]
    public void ExplicitExistingRootIsPrivateAndChildScoped()
    {
        using var fixture = new RootFixture();
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var start = ClaudeReadCommandRunner.CreateStartInfo(fixture.Executable, fixture.Directory.FullName, ClaudeReadOperation.AgentView);
        Assert.NotNull(start);
        Assert.Equal(fixture.Directory.FullName, start.Environment["CLAUDE_CONFIG_DIR"]);
        Assert.False(start.UseShellExecute);
        Assert.True(start.RedirectStandardInput);
        Assert.Equal(previous, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));
    }

    [Fact]
    public async Task NonexistentRelativeAndEmptyRootsCannotActivateASource()
    {
        using var fixture = new RootFixture();
        var missing = Path.Combine(fixture.Directory.FullName, "missing");
        var empty = Directory.CreateDirectory(Path.Combine(fixture.Directory.FullName, "empty"));
        foreach (var root in new[] { missing, "relative", " ", empty.FullName })
        {
            var runner = new ClaudeReadCommandRunner(fixture.Executable, root, TimeSpan.FromSeconds(1), 1024);
            var result = await runner.ReadAsync(ClaudeReadOperation.AgentView, TestContext.Current.CancellationToken);
            Assert.Equal(ClaudeObservation.FailureReason.InvalidConfigurationRoot, result.Failure);
            Assert.Null(result.Output);
        }

        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task OutputBudgetCancelsAtTheBoundaryAndDiscardsStderr()
    {
        using var deadline = new CancellationTokenSource();
        var budget = new ClaudeReadCommandRunner.OutputBudget(16, deadline);
        using var excessive = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ClaudeReadCommandRunner.ReadBoundedAsync(excessive, budget, true, deadline.Token));
        Assert.True(budget.Exceeded);
        using var errorBudget = new CancellationTokenSource();
        using var error = new MemoryStream(Encoding.UTF8.GetBytes("CANARY"));
        var discarded = await ClaudeReadCommandRunner.ReadBoundedAsync(
            error, new(100, errorBudget), false, errorBudget.Token);
        Assert.Empty(discarded);
    }

    [Fact]
    public void RawCommandOutputHasNoSerializationOrToStringRoute()
    {
        var result = new ClaudeCommandResult(
            ClaudeObservation.FailureReason.CommandFailed,
            ClaudeObservation.RootScope.DefaultRoot,
            Encoding.UTF8.GetBytes("CANARY"));
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain("CANARY", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreCancelledReadReturnsCodeWithoutSourceErrorText()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")!;
        var runner = new ClaudeReadCommandRunner(host, Environment.GetEnvironmentVariable("DOTNET_CLI_HOME"), TimeSpan.FromSeconds(5), 1024);
        var result = await runner.ReadAsync(ClaudeReadOperation.Version, cancelled.Token);
        Assert.Equal(ClaudeObservation.FailureReason.Cancelled, result.Failure);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task ExpiredBudgetTerminatesOnlyItsOwnedReadCommand()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")!;
        var runner = new ClaudeReadCommandRunner(host, Environment.GetEnvironmentVariable("DOTNET_CLI_HOME"), TimeSpan.Zero, 1024);
        var result = await runner.ReadAsync(ClaudeReadOperation.Version, TestContext.Current.CancellationToken);
        Assert.Equal(ClaudeObservation.FailureReason.Timeout, result.Failure);
        Assert.Null(result.Output);
    }

    private sealed class RootFixture : IDisposable
    {
        internal RootFixture()
        {
            this.Directory = System.IO.Directory.CreateTempSubdirectory("workbench-claude-root-");
            this.Executable = Path.Combine(this.Directory.FullName, "claude.exe");
            File.WriteAllText(this.Executable, string.Empty);
        }

        internal DirectoryInfo Directory { get; }

        internal string Executable { get; }

        public void Dispose() => this.Directory.Delete(recursive: true);
    }
}

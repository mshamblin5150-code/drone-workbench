namespace Workbench.Host.Tests.Channel;

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Workbench.Core;
using Workbench.Host.Channel;
using Workbench.Host.Observation;
using Workbench.Host.Tests.Observation;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class LocalChannelServerTests
{
    [Fact]
    public void PipeDaclIsProtectedAndHoldsOnlyCurrentUserAllowAndNetworkDeny()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var pipe = LocalChannelServer.CreateInstance(NewPipeName(), first: true);
        var security = pipe.GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule => Assert.False(rule.IsInherited));
        var allow = Assert.Single(rules, rule => rule.AccessControlType == AccessControlType.Allow);
        Assert.Equal(identity.User, allow.IdentityReference);
        Assert.Equal(
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize,
            allow.PipeAccessRights);
        var deny = Assert.Single(rules, rule => rule.AccessControlType == AccessControlType.Deny);
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), deny.IdentityReference);
    }

    [Fact]
    public async Task SourcesRequestReturnsTheSchemaOneSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = NewPipeName();
        var store = new ObservationStore();
        store.Publish(SyntheticClaude.PartialSuccess(SyntheticClaude.Start));
        var settings = new ObservationSettings(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));
        using var server = LocalChannelServer.Claim(
            name,
            () => SourcesResponse.Serialize(store.Current, settings, SyntheticClaude.Start),
            TimeSpan.FromSeconds(5));
        await server.StartAsync(cancellationToken);
        var response = await ExchangeAsync(name, LocalChannel.SourcesRequest + "\n", cancellationToken);
        await server.StopAsync(cancellationToken);
        using var json = JsonDocument.Parse(response!);
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("revision").GetInt64());
        var claude = Assert.Single(json.RootElement.GetProperty("sources").EnumerateArray().ToList());
        Assert.Equal("Fresh", claude.GetProperty("freshness").GetString());
    }

    [Theory]
    [InlineData("{\"schema\":1,\"request\":\"stop\"}\n")]
    [InlineData("{\"schema\":1, \"request\":\"sources\"}\n")]
    [InlineData("sources\n")]
    [InlineData("\n")]
    public async Task OtherRequestsGetOnlyTheErrorLine(string request)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = NewPipeName();
        var snapshots = 0;
        using var server = LocalChannelServer.Claim(name, () => $"{Interlocked.Increment(ref snapshots)}", TimeSpan.FromSeconds(5));
        await server.StartAsync(cancellationToken);
        Assert.Equal(LocalChannel.UnsupportedRequestResponse, await ExchangeAsync(name, request, cancellationToken));
        await server.StopAsync(cancellationToken);
        Assert.Equal(0, snapshots);
    }

    [Fact]
    public async Task OversizedRequestGetsTheErrorLineWithoutASnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = NewPipeName();
        var snapshots = 0;
        using var server = LocalChannelServer.Claim(name, () => $"{Interlocked.Increment(ref snapshots)}", TimeSpan.FromSeconds(5));
        await server.StartAsync(cancellationToken);
        var oversized = LocalChannel.SourcesRequest + new string(' ', LocalChannel.RequestByteLimit) + "\n";
        Assert.Equal(LocalChannel.UnsupportedRequestResponse, await ExchangeAsync(name, oversized, cancellationToken));
        await server.StopAsync(cancellationToken);
        Assert.Equal(0, snapshots);
    }

    [Fact]
    public async Task SilentClientDoesNotBlockAnotherClientAndIsDroppedWithoutASnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = NewPipeName();
        var timeout = TimeSpan.FromSeconds(3);
        using var server = LocalChannelServer.Claim(name, () => """{"schema":1,"sources":[]}""", timeout);
        await server.StartAsync(cancellationToken);
        using var silent = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await silent.ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken);
        var elapsed = Stopwatch.StartNew();
        var answer = await ExchangeAsync(name, LocalChannel.SourcesRequest + "\n", cancellationToken);
        Assert.Equal("""{"schema":1,"sources":[]}""", answer);
        Assert.True(elapsed.Elapsed < timeout, $"The second client waited {elapsed.Elapsed}.");
        Assert.Equal(0, await silent.ReadAsync(new byte[64], cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
        await server.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task AbandonedConnectionsDoNotStopTheChannel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = NewPipeName();
        using var server = LocalChannelServer.Claim(name, () => """{"schema":1,"sources":[]}""", TimeSpan.FromSeconds(5));
        await server.StartAsync(cancellationToken);
        for (var round = 0; round < 20; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                using var abandoned = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await abandoned.ConnectAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }));
        }

        Assert.Equal("""{"schema":1,"sources":[]}""", await ExchangeAsync(name, LocalChannel.SourcesRequest + "\n", cancellationToken));
        Assert.False(server.ExecuteTask!.IsCompleted);
        await server.StopAsync(cancellationToken);
    }

    [Fact]
    public void SecondServerForTheSameNameFailsToStart()
    {
        var name = NewPipeName();
        using var first = LocalChannelServer.Claim(name, () => string.Empty, TimeSpan.FromSeconds(1));
        var error = Record.Exception(() => LocalChannelServer.Claim(name, () => string.Empty, TimeSpan.FromSeconds(1)));
        Assert.True(error is UnauthorizedAccessException or IOException, error?.GetType().FullName);
    }

    [Fact]
    public async Task FailedSnapshotIsReportedAndTheNextClientIsServed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = NewPipeName();
        var calls = 0;
        using var server = LocalChannelServer.Claim(
            name,
            () => Interlocked.Increment(ref calls) == 1
                ? throw new InvalidOperationException("Synthetic snapshot failure.")
                : """{"schema":1,"sources":[]}""",
            TimeSpan.FromSeconds(5));
        await server.StartAsync(cancellationToken);
        Assert.Equal(LocalChannel.SnapshotUnavailableResponse, await ExchangeAsync(name, LocalChannel.SourcesRequest + "\n", cancellationToken));
        Assert.Equal("""{"schema":1,"sources":[]}""", await ExchangeAsync(name, LocalChannel.SourcesRequest + "\n", cancellationToken));
        Assert.False(server.ExecuteTask!.IsCompleted);
        await server.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task SilentClientsOnEveryInstanceDelayButDoNotStopALaterClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = NewPipeName();
        var timeout = TimeSpan.FromMilliseconds(300);
        using var server = LocalChannelServer.Claim(name, () => """{"schema":1,"sources":[]}""", timeout);
        await server.StartAsync(cancellationToken);
        var silent = new List<NamedPipeClientStream>();
        try
        {
            for (var index = 0; index < LocalChannelServer.MaxInstances; index++)
            {
                var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                silent.Add(client);
                await client.ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }

            Assert.Equal("""{"schema":1,"sources":[]}""", await ExchangeAsync(name, LocalChannel.SourcesRequest + "\n", cancellationToken));
            foreach (var client in silent)
            {
                Assert.Equal(0, await client.ReadAsync(new byte[64], cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
            }

            Assert.False(server.ExecuteTask!.IsCompleted);
        }
        finally
        {
            silent.ForEach(client => client.Dispose());
        }

        await server.StopAsync(cancellationToken);
    }

    private static string NewPipeName() => LocalChannel.PipeName("S-1-5-21-0-0-0-1000", "t" + Guid.NewGuid().ToString("N")[..15]);

    private static async Task<string?> ExchangeAsync(string name, string request, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await client.WriteAsync(Encoding.UTF8.GetBytes(request), cancellationToken);
        await client.FlushAsync(cancellationToken);
        using var reader = new StreamReader(client, Encoding.UTF8);
        return await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
}

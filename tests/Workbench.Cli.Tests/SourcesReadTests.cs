namespace Workbench.Cli.Tests;

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Workbench.Core;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class SourcesReadTests
{
    private const string Snapshot = """{"schema":1,"sources":[]}""";

    [Fact]
    public async Task OwnerVerifiedHostAnswerIsTheSnapshot()
    {
        var (exitCode, output) = await ReadFromTestHostAsync(Snapshot, TestContext.Current.CancellationToken);
        Assert.Equal(SourcesCommand.HostAnswered, exitCode);
        Assert.Equal(Snapshot, output);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData(LocalChannel.UnsupportedRequestResponse)]
    [InlineData(LocalChannel.SnapshotUnavailableResponse)]
    public async Task AnswerThatIsNotASnapshotIsAProtocolError(string answer)
    {
        var (exitCode, output) = await ReadFromTestHostAsync(answer, TestContext.Current.CancellationToken);
        AssertProtocolError(exitCode, output);
    }

    [Fact]
    public async Task OverLimitAnswerIsAProtocolError()
    {
        var answer = """{"schema":1,"sources":[],"padding":""" + "\"" + new string('a', SourcesCommand.ResponseByteLimit) + "\"}";
        var (exitCode, output) = await ReadFromTestHostAsync(answer, TestContext.Current.CancellationToken);
        AssertProtocolError(exitCode, output);
    }

    private static void AssertProtocolError(int exitCode, string output)
    {
        Assert.Equal(SourcesCommand.InvalidResponse, exitCode);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("hostReachable").GetBoolean());
        Assert.Equal("ProtocolError", json.RootElement.GetProperty("reason").GetString());
        Assert.False(json.RootElement.TryGetProperty("sources", out _));
    }

    private static async Task<(int ExitCode, string Output)> ReadFromTestHostAsync(string answer, CancellationToken cancellationToken)
    {
        var name = "drone-workbench-test-" + Guid.NewGuid().ToString("N");
        await using var server = NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            CurrentUserSecurity());
        var serving = ServeOnceAsync(server, answer, cancellationToken);
        var result = await SourcesCommand.ReadAsync(name, cancellationToken);
        await serving.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return result;
    }

    private static async Task ServeOnceAsync(NamedPipeServerStream server, string answer, CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        try
        {
            var request = new byte[LocalChannel.SourcesRequest.Length + 1];
            await server.ReadExactlyAsync(request, cancellationToken);
            Assert.Equal(LocalChannel.SourcesRequest + "\n", Encoding.UTF8.GetString(request));
            await server.WriteAsync(Encoding.UTF8.GetBytes(answer + "\n"), cancellationToken);
            await server.FlushAsync(cancellationToken);
            while (await server.ReadAsync(new byte[64], cancellationToken) > 0)
            {
            }
        }
        catch (IOException)
        {
        }
    }

    private static PipeSecurity CurrentUserSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }
}

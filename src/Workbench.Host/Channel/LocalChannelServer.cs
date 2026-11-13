namespace Workbench.Host.Channel;

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Hosting;
using Workbench.Core;

[SupportedOSPlatform("windows")]
internal sealed class LocalChannelServer : BackgroundService
{
    internal const int MaxInstances = 4;

    private const int ResponseBufferSize = 64 * 1024;

    private static readonly TimeSpan CreateRetryDelay = TimeSpan.FromSeconds(1);

    private static readonly byte[] Request = Encoding.UTF8.GetBytes(LocalChannel.SourcesRequest);

    private readonly string pipeName;
    private readonly Func<string> respond;
    private readonly TimeSpan connectionTimeout;
    private readonly SemaphoreSlim connectionSlots = new(MaxInstances - 1, MaxInstances - 1);
    private NamedPipeServerStream? listening;

    private LocalChannelServer(string pipeName, Func<string> respond, TimeSpan connectionTimeout, NamedPipeServerStream first)
    {
        this.pipeName = pipeName;
        this.respond = respond;
        this.connectionTimeout = connectionTimeout;
        this.listening = first;
    }

    public override void Dispose()
    {
        this.listening?.Dispose();
        this.listening = null;
        this.connectionSlots.Dispose();
        base.Dispose();
    }

    internal static LocalChannelServer Claim(string pipeName, Func<string> respond, TimeSpan connectionTimeout) =>
        new(pipeName, respond, connectionTimeout, CreateInstance(pipeName, first: true));

    internal static PipeSecurity CreateSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current Windows user has no SID.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        return security;
    }

    internal static NamedPipeServerStream CreateInstance(string pipeName, bool first) =>
        NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            MaxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            LocalChannel.RequestByteLimit,
            ResponseBufferSize,
            CreateSecurity());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connections = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var pipe = this.listening ??= await this.CreateReplacementAsync(stoppingToken);
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);
                }
                catch (IOException)
                {
                    // A client that closed before the connection was accepted must not stop the channel.
                    this.listening = this.TryCreateReplacement();
                    await pipe.DisposeAsync();
                    continue;
                }

                await this.connectionSlots.WaitAsync(stoppingToken);
                this.listening = await this.CreateReplacementAsync(stoppingToken);
                connections.RemoveAll(connection => connection.IsCompleted);
                connections.Add(this.ServeAsync(pipe, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            this.listening?.Dispose();
            this.listening = null;
            await Task.WhenAll(connections);
        }
    }

    private static async Task<bool> ReadRequestAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var buffer = new byte[LocalChannel.RequestByteLimit + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await pipe.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (count == 0)
            {
                break;
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', length, count);
            length += count;
            if (newline >= 0)
            {
                length = newline;
                break;
            }
        }

        if (length > LocalChannel.RequestByteLimit)
        {
            return false;
        }

        var line = buffer.AsSpan(0, length);
        if (line.EndsWith("\r"u8))
        {
            line = line[..^1];
        }

        return line.SequenceEqual(Request);
    }

    private static async Task WaitForClientCloseAsync(Stream pipe, CancellationToken token)
    {
        var buffer = new byte[64];
        while (await pipe.ReadAsync(buffer, token) > 0)
        {
        }
    }

    private NamedPipeServerStream? TryCreateReplacement()
    {
        try
        {
            return CreateInstance(this.pipeName, first: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<NamedPipeServerStream> CreateReplacementAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            if (this.TryCreateReplacement() is { } replacement)
            {
                return replacement;
            }

            await Task.Delay(CreateRetryDelay, stoppingToken);
        }
    }

    private string Snapshot()
    {
        try
        {
            return this.respond();
        }
        catch (Exception)
        {
            return LocalChannel.SnapshotUnavailableResponse;
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(this.connectionTimeout);
            var response = await ReadRequestAsync(pipe, deadline.Token)
                ? this.Snapshot()
                : LocalChannel.UnsupportedRequestResponse;
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"), deadline.Token);
            await pipe.FlushAsync(deadline.Token);
            await WaitForClientCloseAsync(pipe, deadline.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            try
            {
                await pipe.DisposeAsync();
            }
            finally
            {
                this.connectionSlots.Release();
            }
        }
    }
}

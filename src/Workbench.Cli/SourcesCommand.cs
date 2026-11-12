namespace Workbench.Cli;

using System.CommandLine;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Workbench.Core;

internal static class SourcesCommand
{
    internal const int HostAnswered = 0;

    internal const int UsageError = 2;

    internal const int HostUnreachable = 3;

    internal const int InvalidResponse = 4;

    internal const int Cancelled = 130;

    internal const int ResponseByteLimit = 1024 * 1024;

    private const string CancelledOutput = """{"schema":1,"reason":"Cancelled"}""";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    internal static Command Create()
    {
        var json = new Option<bool>("--json") { Description = "Write the snapshot as JSON (required)." };
        var command = new Command("sources", "Read the latest source snapshot from the running host.");
        command.Options.Add(json);
        command.SetAction(async (result, cancellationToken) =>
        {
            if (!result.GetValue(json))
            {
                await Console.Error.WriteLineAsync("Usage: drone-workbench sources --json (JSON is the only supported output).");
                return UsageError;
            }

            string pipeName;
            try
            {
                pipeName = LocalChannel.PipeNameForCurrentUser();
            }
            catch (ArgumentException error)
            {
                await Console.Error.WriteLineAsync(error.Message);
                return UsageError;
            }

            var (exitCode, output) = await ReadAsync(pipeName, cancellationToken);
            Console.WriteLine(output);
            return exitCode;
        });
        return command;
    }

    internal static async Task<(int ExitCode, string Output)> ReadAsync(string pipeName, CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(ConnectTimeout, cancellationToken);
            if (!IsOwnedByCurrentUser(pipe))
            {
                return (HostUnreachable, Unreachable("UntrustedOrDenied"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (Cancelled, CancelledOutput);
        }
        catch (TimeoutException)
        {
            return (HostUnreachable, Unreachable("NoHostListening"));
        }
        catch (UnauthorizedAccessException)
        {
            return (HostUnreachable, Unreachable("UntrustedOrDenied"));
        }
        catch (IOException)
        {
            return (HostUnreachable, Unreachable("ConnectFailed"));
        }

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ResponseTimeout);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(LocalChannel.SourcesRequest + "\n"), deadline.Token);
            await pipe.FlushAsync(deadline.Token);
            var line = await ReadLineAsync(pipe, deadline.Token);
            if (line is not null && IsSnapshot(line))
            {
                return (HostAnswered, line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (Cancelled, CancelledOutput);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException)
        {
        }

        return (InvalidResponse, """{"schema":1,"hostReachable":true,"reason":"ProtocolError"}""");
    }

    private static string Unreachable(string reason) =>
        $$"""{"schema":1,"hostReachable":false,"reason":"{{reason}}"}""";

    private static bool IsOwnedByCurrentUser(PipeStream pipe)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        return identity.User is { } user && user.Equals(owner);
    }

    private static async Task<string?> ReadLineAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var response = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await pipe.ReadAsync(buffer, cancellationToken);
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
            response.Write(buffer, 0, newline >= 0 ? newline : count);
            if (response.Length > ResponseByteLimit)
            {
                return null;
            }

            if (newline >= 0 || count == 0)
            {
                return Encoding.UTF8.GetString(response.GetBuffer(), 0, (int)response.Length).TrimEnd('\r');
            }
        }
    }

    private static bool IsSnapshot(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("schema", out var schema) &&
                schema.ValueKind == JsonValueKind.Number &&
                schema.TryGetInt32(out var version) &&
                version == LocalChannel.SchemaVersion &&
                root.TryGetProperty("sources", out var sources) &&
                sources.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

namespace Workbench.Host.Sources.Claude;

using System.ComponentModel;
using System.Diagnostics;
using System.Security;

internal sealed class ClaudeReadCommandRunner(
    string? executable,
    string? configuredRoot,
    TimeSpan timeout,
    int outputLimit) : IClaudeCommandReader
{
    internal ClaudeReadCommandRunner()
        : this(
            FindExecutable(Environment.GetEnvironmentVariable("PATH")),
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
            TimeSpan.FromSeconds(10),
            1024 * 1024)
    {
    }

    public async Task<ClaudeCommandResult> ReadAsync(
        ClaudeReadOperation operation,
        CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        var scope = string.IsNullOrEmpty(configuredRoot)
            ? ClaudeObservation.RootScope.DefaultRoot
            : ClaudeObservation.RootScope.ConfiguredRoot;
        if (cancellationToken.IsCancellationRequested)
        {
            return new(ClaudeObservation.FailureReason.Cancelled, scope);
        }

        if (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            return new(ClaudeObservation.FailureReason.MissingExecutable, scope);
        }

        try
        {
            var start = CreateStartInfo(executable, configuredRoot, operation);
            if (start is null)
            {
                return new(ClaudeObservation.FailureReason.InvalidConfigurationRoot, scope);
            }

            using var process = new Process { StartInfo = start };
            if (!process.Start())
            {
                return new(ClaudeObservation.FailureReason.CommandFailed, scope);
            }

            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            var budget = new OutputBudget(outputLimit, deadline);
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, budget, true, deadline.Token);
            var errors = ReadBoundedAsync(process.StandardError.BaseStream, budget, false, deadline.Token);
            try
            {
                await Task.WhenAll(
                    output,
                    errors,
                    process.WaitForExitAsync(deadline.Token)).WaitAsync(deadline.Token);
                return process.ExitCode == 0
                    ? new(ClaudeObservation.FailureReason.None, scope, await output)
                    : new(ClaudeObservation.FailureReason.CommandFailed, scope);
            }
            catch (OperationCanceledException)
            {
                await EndOwnedQueryAsync(process);
                await ObserveReadersAsync(output, errors);
                var failure = budget.Exceeded
                    ? ClaudeObservation.FailureReason.OutputTooLarge
                    : cancellationToken.IsCancellationRequested
                        ? ClaudeObservation.FailureReason.Cancelled
                        : ClaudeObservation.FailureReason.Timeout;
                return new(failure, scope);
            }
            catch (IOException)
            {
                await EndOwnedQueryAsync(process);
                await ObserveReadersAsync(output, errors);
                return new(ClaudeObservation.FailureReason.CommandFailed, scope);
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return new(ClaudeObservation.FailureReason.CommandFailed, scope);
        }
    }

    internal static string? FindExecutable(string? path)
    {
        foreach (var directory in (path ?? string.Empty).Split(Path.PathSeparator))
        {
            var candidateDirectory = directory.Trim('"');
            if (!Path.IsPathFullyQualified(candidateDirectory))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(candidateDirectory, "claude.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    internal static ProcessStartInfo? CreateStartInfo(
        string executablePath,
        string? root,
        ClaudeReadOperation operation,
        string? defaultRoot = null)
    {
        ValidateOperation(operation);
        var effectiveRoot = string.IsNullOrEmpty(root)
            ? defaultRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : root;
        if (!Path.IsPathFullyQualified(effectiveRoot) ||
            !Directory.Exists(effectiveRoot) ||
            !Directory.EnumerateFileSystemEntries(effectiveRoot).Any())
        {
            return null;
        }

        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = effectiveRoot,
        };
        if (string.IsNullOrEmpty(root))
        {
            start.Environment.Remove("CLAUDE_CONFIG_DIR");
        }
        else
        {
            start.Environment["CLAUDE_CONFIG_DIR"] = Path.GetFullPath(root);
        }

        foreach (var argument in ArgumentsFor(operation))
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    internal static IReadOnlyList<string> ArgumentsFor(ClaudeReadOperation operation) => operation switch
    {
        ClaudeReadOperation.Version => ["--version"],
        ClaudeReadOperation.AgentView => ["agents", "--json", "--all"],
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        OutputBudget budget,
        bool retain,
        CancellationToken cancellationToken)
    {
        using var result = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                return result.ToArray();
            }

            if (!budget.Admit(count))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (retain)
            {
                result.Write(buffer, 0, count);
            }
        }
    }

    private static void ValidateOperation(ClaudeReadOperation operation)
    {
        if (operation is not (ClaudeReadOperation.Version or ClaudeReadOperation.AgentView))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task EndOwnedQueryAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: false);
        }

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task ObserveReadersAsync(params Task[] readers)
    {
        try
        {
            await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or TimeoutException)
        {
        }
    }

    internal sealed class OutputBudget(int limit, CancellationTokenSource cancellation)
    {
        private int consumed;
        private int exceeded;

        internal bool Exceeded => Volatile.Read(ref this.exceeded) != 0;

        internal bool Admit(int count)
        {
            if (Interlocked.Add(ref this.consumed, count) <= limit)
            {
                return true;
            }

            Interlocked.Exchange(ref this.exceeded, 1);
            cancellation.Cancel();
            return false;
        }
    }
}

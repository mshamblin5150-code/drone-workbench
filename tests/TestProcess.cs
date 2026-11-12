namespace Workbench.Tests;

using System.Diagnostics;

internal sealed class TestProcess : IDisposable
{
    private readonly Process process;
    private readonly Task<string> standardError;

    private TestProcess(Process process)
    {
        this.process = process;
        this.standardError = process.StandardError.ReadToEndAsync();
    }

    public StreamReader StandardOutput => this.process.StandardOutput;

    public static TestProcess Start(string component, params string[] arguments) =>
        Start(component, new Dictionary<string, string>(), arguments);

    public static string NewInstance() => "t" + Guid.NewGuid().ToString("N")[..15];

    public static TestProcess Start(string component, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? throw new InvalidOperationException("Run tests with tools/verify-dotnet.ps1 to select the pinned SDK.");
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, $"Workbench.{component}.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:45678";
        start.Environment["Kestrel__Endpoints__Injected__Url"] = "unsupported-scheme://127.0.0.1:45679";
        start.Environment["DRONE_WORKBENCH_INSTANCE"] = NewInstance();
        if (component == "Host")
        {
            HideClaudeInstallation(start);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        return new TestProcess(Process.Start(start) ?? throw new InvalidOperationException("The owned test process did not start."));
    }

    public async Task<IReadOnlyList<string>> ReadUntilStartedAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await this.process.StandardOutput.ReadLineAsync(cancellationToken).AsTask().WaitAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
            if (line.Contains("Application started.", StringComparison.Ordinal))
            {
                return lines;
            }
        }

        throw new InvalidOperationException("The owned host exited before it started.");
    }

    public async Task<ProcessResult> CompleteAsync()
    {
        var output = this.process.StandardOutput.ReadToEndAsync();
        await this.process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return new ProcessResult(this.process.ExitCode, await output, await this.standardError);
    }

    public void Dispose()
    {
        if (!this.process.HasExited)
        {
            this.process.Kill(entireProcessTree: true);
            this.process.WaitForExit(5000);
        }

        this.process.Dispose();
    }

    private static void HideClaudeInstallation(ProcessStartInfo start) => start.Environment["PATH"] = string.Empty;

    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

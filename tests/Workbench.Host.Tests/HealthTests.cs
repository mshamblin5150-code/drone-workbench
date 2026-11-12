namespace Workbench.Host.Tests;

using System.Net;
using System.Text.Json;
using Workbench.Tests;
using Xunit;

public sealed class HealthTests
{
    [Fact]
    public async Task HostRejectsListenerArguments()
    {
        using var process = TestProcess.Start("Host", "--urls", "http://127.0.0.1:45678");
        var result = await process.CompleteAsync();
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("Usage:", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostRefusesAnInvalidInstanceSuffix()
    {
        using var process = TestProcess.Start("Host", new Dictionary<string, string> { ["DRONE_WORKBENCH_INSTANCE"] = "UPPER" });
        var result = await process.CompleteAsync();
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("DRONE_WORKBENCH_INSTANCE", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnedHostServesHealthOnLoopbackOnly()
    {
        using var process = TestProcess.Start("Host");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var endpoints = new List<Uri>();
        foreach (var line in await process.ReadUntilStartedAsync(deadline.Token))
        {
            const string listening = "Now listening on: ";
            var index = line.IndexOf(listening, StringComparison.Ordinal);
            if (index >= 0)
            {
                endpoints.Add(new Uri(line[(index + listening.Length)..].Trim()));
            }
        }

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.NotEqual(45678, endpoint.Port);
        using var client = new HttpClient { BaseAddress = endpoint };
        using var response = await client.GetAsync("/health", deadline.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var health = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
        Assert.Equal("ok", health.RootElement.GetProperty("status").GetString());
        Assert.Equal("foundation", health.RootElement.GetProperty("stage").GetString());
        Assert.True(health.RootElement.GetProperty("collectionAvailable").GetBoolean());
        Assert.False(health.RootElement.GetProperty("controlsAvailable").GetBoolean());
        using var missing = await client.GetAsync("/sessions", deadline.Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}

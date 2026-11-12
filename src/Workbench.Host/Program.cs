using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core;
using Workbench.Host.Channel;
using Workbench.Host.Observation;
using Workbench.Host.Sources.Claude;

if (args is ["observe-claude", "--json"])
{
    var observation = await new ClaudeObservationSource().CaptureAsync(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(observation, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    }));
    return observation.State is ClaudeObservation.CaptureState.Observed or ClaudeObservation.CaptureState.Partial ? 0 : 1;
}

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: Workbench.Host | Workbench.Host observe-claude --json");
    return 2;
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Workbench.Host requires Windows.");
    return 1;
}

string pipeName;
try
{
    pipeName = LocalChannel.PipeNameForCurrentUser();
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

var settings = ObservationSettings.Default;
var store = new ObservationStore();
LocalChannelServer channel;
try
{
    channel = LocalChannelServer.Claim(
        pipeName,
        () => SourcesResponse.Serialize(store.Current, settings, TimeProvider.System.GetUtcNow()),
        TimeSpan.FromSeconds(2));
}
catch (Exception error) when (error is UnauthorizedAccessException or IOException)
{
    Console.Error.WriteLine("Another Workbench host already owns the local channel for this Windows user.");
    return 1;
}

var builder = WebApplication.CreateSlimBuilder();
builder.Configuration.Sources.Clear();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
builder.Services.AddHostedService(_ => new ObservationLoop(new ClaudeObservationSource(), store, settings, TimeProvider.System));
builder.Services.AddHostedService(_ => channel);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    stage = Foundation.Stage,
    version = Foundation.Version,
    collectionAvailable = true,
    controlsAvailable = false,
}));
await app.RunAsync();
return 0;

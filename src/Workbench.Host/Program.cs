using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core;
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

var builder = WebApplication.CreateSlimBuilder();
builder.Configuration.Sources.Clear();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    stage = Foundation.Stage,
    version = Foundation.Version,
    collectionAvailable = false,
    controlsAvailable = false,
}));
await app.RunAsync();
return 0;

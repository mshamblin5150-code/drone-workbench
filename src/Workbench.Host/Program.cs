using System.Net;
using Workbench.Core;

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: Workbench.Host (binds an ephemeral IPv4 loopback port)");
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

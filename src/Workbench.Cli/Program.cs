using System.CommandLine;
using System.Text.Json;
using Workbench.Core;

var root = new RootCommand("Drone Workbench foundation diagnostics. Collection and controls are not available.");
var diagnostics = new Command("diagnostics", "Report this CLI's foundation capabilities without contacting a host.");
var json = new Option<bool>("--json") { Description = "Write machine-readable diagnostics." };
diagnostics.Options.Add(json);
diagnostics.SetAction(result =>
{
    if (result.GetValue(json))
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            component = "cli",
            stage = Foundation.Stage,
            version = Foundation.Version,
            hostCommunicationAvailable = false,
            collectionAvailable = false,
            controlsAvailable = false,
        }));
    }
    else
    {
        Console.WriteLine($"Drone Workbench {Foundation.Version} ({Foundation.Stage})");
        Console.WriteLine("Host communication, collection, and controls are not available.");
    }
});
root.Subcommands.Add(diagnostics);
return root.Parse(args).Invoke();

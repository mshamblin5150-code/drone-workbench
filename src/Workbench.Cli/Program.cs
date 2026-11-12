using System.CommandLine;
using System.Text.Json;
using Workbench.Cli;
using Workbench.Core;

var root = new RootCommand("Drone Workbench CLI. Reads the running host's source snapshot; controls are not available.");
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
            hostCommunicationAvailable = true,
            collectionAvailable = false,
            controlsAvailable = false,
        }));
    }
    else
    {
        Console.WriteLine($"Drone Workbench {Foundation.Version} ({Foundation.Stage})");
        Console.WriteLine("The host collects; this CLI reads its snapshot with `sources --json`. Controls are not available.");
    }
});
root.Subcommands.Add(diagnostics);
root.Subcommands.Add(SourcesCommand.Create());
return await root.Parse(args).InvokeAsync();

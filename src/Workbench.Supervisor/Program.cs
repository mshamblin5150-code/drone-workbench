using System.Text.Json;
using Workbench.Core;

if (args is not ["diagnostics"])
{
    Console.Error.WriteLine("Usage: Workbench.Supervisor diagnostics");
    return 2;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    component = "supervisor",
    stage = Foundation.Stage,
    version = Foundation.Version,
    supervisionAvailable = false,
    startupRegistrationAvailable = false,
}));
return 0;

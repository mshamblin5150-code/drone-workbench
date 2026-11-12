namespace Workbench.Host.Sources.Claude;

internal interface IClaudeProcessReader
{
    ClaudeProcessInventory Capture();
}

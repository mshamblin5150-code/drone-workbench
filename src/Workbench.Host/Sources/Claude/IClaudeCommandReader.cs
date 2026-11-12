namespace Workbench.Host.Sources.Claude;

internal interface IClaudeCommandReader
{
    Task<ClaudeCommandResult> ReadAsync(ClaudeReadOperation operation, CancellationToken cancellationToken);
}

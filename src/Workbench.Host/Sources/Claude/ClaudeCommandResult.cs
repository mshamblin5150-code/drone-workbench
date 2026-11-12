namespace Workbench.Host.Sources.Claude;

using System.Text.Json.Serialization;

internal sealed class ClaudeCommandResult(
    ClaudeObservation.FailureReason failure,
    ClaudeObservation.RootScope scope,
    byte[]? output = null)
{
    public ClaudeObservation.FailureReason Failure { get; } = failure;

    public ClaudeObservation.RootScope Scope { get; } = scope;

    [JsonIgnore]
    public byte[]? Output { get; } = output;

    public override string ToString() => this.Failure.ToString();
}

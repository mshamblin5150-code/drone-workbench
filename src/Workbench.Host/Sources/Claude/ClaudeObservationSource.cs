namespace Workbench.Host.Sources.Claude;

using System.Text;
using System.Text.RegularExpressions;

internal sealed partial class ClaudeObservationSource(
    IClaudeCommandReader commands,
    IClaudeProcessReader processes,
    TimeProvider clock)
{
    internal ClaudeObservationSource()
        : this(new ClaudeReadCommandRunner(), new WindowsClaudeProcessReader(), TimeProvider.System)
    {
    }

    internal static ClaudeObservation.SessionSummary Reconcile(
        ClaudeAgentEntry entry,
        ClaudeProcessInventory inventory,
        DateTimeOffset attemptedAt)
    {
        var processMatched = entry.ProcessId is { } pid &&
            inventory.Processes.TryGetValue(pid, out var process) &&
            process.IsClaude && process.CreatedAt is { } createdAt && createdAt <= attemptedAt;
        var entrypoint = entry.Kind == ClaudeAgentEntry.AgentKind.Background
            ? ClaudeObservation.Entrypoint.Background
            : entry.Kind == ClaudeAgentEntry.AgentKind.Interactive && processMatched
                ? Classify(entry.ProcessId!.Value, inventory)
                : ClaudeObservation.Entrypoint.Unknown;
        var state = entry.HasUnknownValues
            ? ClaudeObservation.WorkState.Unknown
            : processMatched && !entry.Stopped
                ? entry.Status
                : entry.Stopped && entry.ProcessId is null
                    ? ClaudeObservation.WorkState.Stopped
                    : ClaudeObservation.WorkState.Unknown;
        return new(entrypoint, state, processMatched);
    }

    internal async Task<ClaudeObservation> CaptureAsync(CancellationToken cancellationToken)
    {
        var attemptedAt = clock.GetUtcNow();
        var scope = ClaudeObservation.RootScope.DefaultRoot;
        string? version = null;
        try
        {
            var versionRead = await commands.ReadAsync(ClaudeReadOperation.Version, cancellationToken);
            scope = versionRead.Scope;
            if (versionRead.Failure != ClaudeObservation.FailureReason.None)
            {
                return Failure(versionRead.Failure, attemptedAt, scope);
            }

            var versionText = versionRead.Output is { Length: <= 128 } bytes
                ? Encoding.UTF8.GetString(bytes).Trim()
                : string.Empty;
            var match = VersionPattern().Match(versionText);
            if (!match.Success)
            {
                return Failure(ClaudeObservation.FailureReason.UnsupportedVersion, attemptedAt, scope);
            }

            version = match.Groups[1].Value;
            if (version is not ("2.1.312" or "2.1.322" or "2.1.323"))
            {
                return Failure(ClaudeObservation.FailureReason.UnsupportedVersion, attemptedAt, scope, version);
            }

            var agentRead = await commands.ReadAsync(ClaudeReadOperation.AgentView, cancellationToken);
            if (agentRead.Failure != ClaudeObservation.FailureReason.None)
            {
                return Failure(agentRead.Failure, attemptedAt, scope, version);
            }

            var observedAt = clock.GetUtcNow();
            var parsed = ClaudeAgentViewParser.Parse(agentRead.Output ?? []);
            if (parsed.Failure != ClaudeObservation.FailureReason.None)
            {
                return Failure(parsed.Failure, attemptedAt, scope, version);
            }

            var inventory = processes.Capture();
            var summaries = new List<ClaudeObservation.SessionSummary>();
            var matched = new HashSet<int>();
            foreach (var entry in parsed.Entries)
            {
                var summary = Reconcile(entry, inventory, attemptedAt);
                summaries.Add(summary);
                if (summary.ProcessMatched && entry.ProcessId is { } pid)
                {
                    matched.Add(pid);
                }
            }

            var knownUnmatched = inventory.Processes.Values.Count(process =>
                process.IsClaude && process.CreatedAt is not null && !matched.Contains(process.ProcessId));
            var complete = inventory.Complete &&
                inventory.Processes.Values.Where(process => process.IsClaude).All(process => process.CreatedAt is not null);
            var gaps = ClaudeObservation.DefaultGaps.ToList();
            if (!complete)
            {
                gaps.Add(ClaudeObservation.CoverageGap.ProcessInventoryIncomplete);
            }

            if (summaries.Any(session => session.State == ClaudeObservation.WorkState.Unknown))
            {
                gaps.Add(ClaudeObservation.CoverageGap.UnknownSessionState);
            }

            if (parsed.Entries.Any(entry => entry.HasUnknownValues))
            {
                gaps.Add(ClaudeObservation.CoverageGap.SchemaDrift);
            }

            var partial = !complete || summaries.Any(session =>
                session.State == ClaudeObservation.WorkState.Unknown ||
                session.Entrypoint == ClaudeObservation.Entrypoint.Unknown);
            return new(
                partial ? ClaudeObservation.CaptureState.Partial : ClaudeObservation.CaptureState.Observed,
                attemptedAt,
                observedAt,
                version,
                scope,
                summaries,
                new(complete, knownUnmatched, complete ? knownUnmatched : null),
                ClaudeObservation.FailureReason.None,
                gaps);
        }
        catch (OperationCanceledException)
        {
            return Failure(ClaudeObservation.FailureReason.Cancelled, attemptedAt, scope, version);
        }
        catch (Exception)
        {
            return Failure(ClaudeObservation.FailureReason.CommandFailed, attemptedAt, scope, version);
        }
    }

    private static ClaudeObservation.Entrypoint Classify(int pid, ClaudeProcessInventory inventory)
    {
        var subject = inventory.Processes[pid];
        var visited = new HashSet<int> { pid };
        var childCreatedAt = subject.CreatedAt;
        pid = subject.ParentId;
        while (pid != 0 && visited.Add(pid) && inventory.Processes.TryGetValue(pid, out var process))
        {
            if (process.CreatedAt is not { } createdAt ||
                (childCreatedAt is { } childTime && createdAt > childTime))
            {
                return ClaudeObservation.Entrypoint.Unknown;
            }

            if (process.Role == ClaudeProcessInventory.ProcessRole.DesktopApp)
            {
                return ClaudeObservation.Entrypoint.Desktop;
            }

            if (process.Role == ClaudeProcessInventory.ProcessRole.Terminal)
            {
                return ClaudeObservation.Entrypoint.InteractiveCli;
            }

            childCreatedAt = createdAt;
            pid = process.ParentId;
        }

        return ClaudeObservation.Entrypoint.Unknown;
    }

    private static ClaudeObservation Failure(
        ClaudeObservation.FailureReason failure,
        DateTimeOffset attemptedAt,
        ClaudeObservation.RootScope scope,
        string? version = null)
    {
        var state = failure switch
        {
            ClaudeObservation.FailureReason.MissingExecutable or
                ClaudeObservation.FailureReason.InvalidConfigurationRoot => ClaudeObservation.CaptureState.Unavailable,
            ClaudeObservation.FailureReason.UnsupportedVersion => ClaudeObservation.CaptureState.UnsupportedVersion,
            _ => ClaudeObservation.CaptureState.Disconnected,
        };
        return new(state, attemptedAt, null, version, scope, null, new(false, 0, null), failure, ClaudeObservation.DefaultGaps);
    }

    [GeneratedRegex(@"^([0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}) \(Claude Code\)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

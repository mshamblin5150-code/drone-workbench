namespace Workbench.Host.Sources.Claude;

using System.Text.Json;

internal static class ClaudeAgentViewParser
{
    internal static ParseResult Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new(ClaudeObservation.FailureReason.SchemaDrift, []);
            }

            if (document.RootElement.GetArrayLength() > 4096)
            {
                return new(ClaudeObservation.FailureReason.OutputTooLarge, []);
            }

            var entries = new List<ClaudeAgentEntry>();
            var identities = new HashSet<Guid>();
            var pids = new HashSet<int>();
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object ||
                    !TryString(row, "sessionId", out var id) ||
                    !Guid.TryParseExact(id, "D", out var sessionId) ||
                    sessionId == Guid.Empty ||
                    !identities.Add(sessionId) ||
                    !TryString(row, "kind", out var kindText))
                {
                    return new(ClaudeObservation.FailureReason.SchemaDrift, []);
                }

                var properties = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in row.EnumerateObject())
                {
                    if (!properties.Add(property.Name))
                    {
                        return new(ClaudeObservation.FailureReason.SchemaDrift, []);
                    }
                }

                int? processId = null;
                if (row.TryGetProperty("pid", out var pidValue))
                {
                    if (!pidValue.TryGetInt32(out var pid) || pid <= 0 || !pids.Add(pid))
                    {
                        return new(ClaudeObservation.FailureReason.SchemaDrift, []);
                    }

                    processId = pid;
                }

                var kind = kindText switch
                {
                    "interactive" => ClaudeAgentEntry.AgentKind.Interactive,
                    "background" => ClaudeAgentEntry.AgentKind.Background,
                    _ => ClaudeAgentEntry.AgentKind.Unknown,
                };
                var status = ClaudeObservation.WorkState.Unknown;
                var unknown = kind == ClaudeAgentEntry.AgentKind.Unknown;
                if (row.TryGetProperty("status", out var statusValue))
                {
                    if (statusValue.ValueKind != JsonValueKind.String)
                    {
                        return new(ClaudeObservation.FailureReason.SchemaDrift, []);
                    }

                    status = statusValue.GetString() switch
                    {
                        "busy" => ClaudeObservation.WorkState.Busy,
                        "waiting" => ClaudeObservation.WorkState.Waiting,
                        "idle" => ClaudeObservation.WorkState.Idle,
                        _ => ClaudeObservation.WorkState.Unknown,
                    };
                    unknown |= status == ClaudeObservation.WorkState.Unknown;
                }

                var stopped = false;
                if (row.TryGetProperty("state", out var stateValue))
                {
                    if (stateValue.ValueKind != JsonValueKind.String)
                    {
                        return new(ClaudeObservation.FailureReason.SchemaDrift, []);
                    }

                    var state = stateValue.GetString();
                    stopped = state == "stopped";
                    unknown |= state is not ("working" or "blocked" or "done" or "failed" or "stopped");
                }

                unknown |= stopped && (processId is not null || status != ClaudeObservation.WorkState.Unknown);
                entries.Add(new(sessionId, kind, status, stopped, processId, unknown));
            }

            return new(ClaudeObservation.FailureReason.None, entries);
        }
        catch (JsonException)
        {
            return new(ClaudeObservation.FailureReason.MalformedOutput, []);
        }
        catch (InvalidOperationException)
        {
            return new(ClaudeObservation.FailureReason.SchemaDrift, []);
        }
    }

    private static bool TryString(JsonElement row, string field, out string? value)
    {
        value = null;
        if (!row.TryGetProperty(field, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null;
    }

    internal sealed record ParseResult(
        ClaudeObservation.FailureReason Failure,
        IReadOnlyList<ClaudeAgentEntry> Entries);
}

namespace Workbench.Host.Tests.Sources.Claude;

using System.Text;
using System.Text.Json;
using Workbench.Host.Sources.Claude;
using Xunit;

public sealed class ClaudeParserTests
{
    [Fact]
    public void DropsNamesPathsShortIdsUnknownFieldsAndTimestampPayloads()
    {
        var parsed = Parse("""
            [{"sessionId":"26d93c03-641e-4199-a713-f5a32823a171","kind":"interactive",
            "status":"idle","pid":123,"name":"CANARY","cwd":"CANARY",
            "id":"CANARY","startedAt":"CANARY","private":"CANARY"}]
            """);
        Assert.Equal(ClaudeObservation.FailureReason.None, parsed.Failure);
        Assert.Single(parsed.Entries);
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(parsed), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"CANARY\":\"CANARY\"}")]
    [InlineData("[null]")]
    [InlineData("[{\"sessionId\":\"CANARY\",\"kind\":\"interactive\"}]")]
    [InlineData("[{\"sessionId\":\"00000000-0000-0000-0000-000000000000\",\"kind\":\"interactive\"}]")]
    [InlineData("[{\"sessionId\":\"26d93c03-641e-4199-a713-f5a32823a171\",\"kind\":\"interactive\",\"pid\":\"CANARY\"}]")]
    [InlineData("[{\"sessionId\":\"26d93c03-641e-4199-a713-f5a32823a171\",\"kind\":\"interactive\",\"status\":null}]")]
    [InlineData("[{\"sessionId\":\"26d93c03-641e-4199-a713-f5a32823a171\",\"kind\":\"interactive\",\"pid\":-1}]")]
    [InlineData("[{\"sessionId\":\"26d93c03-641e-4199-a713-f5a32823a171\",\"kind\":\"interactive\",\"kind\":\"CANARY\"}]")]
    [InlineData("[{\"sessionId\":\"26d93c03-641e-4199-a713-f5a32823a171\",\"kind\":\"interactive\",\"pid\":123,\"pid\":124}]")]
    public void RejectsMalformedSchemaWithoutEchoingInput(string json)
    {
        var parsed = Parse(json);
        Assert.Equal(ClaudeObservation.FailureReason.SchemaDrift, parsed.Failure);
        Assert.Empty(parsed.Entries);
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(parsed), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedJsonProducesOnlyACode()
    {
        var parsed = Parse("[\"CANARY");
        Assert.Equal(ClaudeObservation.FailureReason.MalformedOutput, parsed.Failure);
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(parsed), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownEnumValuesAreDroppedIntoAnExplicitUnknown()
    {
        var parsed = Parse("""
            [{"sessionId":"26d93c03-641e-4199-a713-f5a32823a171",
            "kind":"CANARY","status":"CANARY","state":"CANARY"}]
            """);
        var entry = Assert.Single(parsed.Entries);
        Assert.Equal(ClaudeAgentEntry.AgentKind.Unknown, entry.Kind);
        Assert.Equal(ClaudeObservation.WorkState.Unknown, entry.Status);
        Assert.True(entry.HasUnknownValues);
        Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(parsed), StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateIdentityOrPidCannotCreateTwoSessions()
    {
        const string row = """{"sessionId":"26d93c03-641e-4199-a713-f5a32823a171","kind":"interactive","pid":123}""";
        Assert.Equal(ClaudeObservation.FailureReason.SchemaDrift, Parse($"[{row},{row}]").Failure);
        var sibling = row.Replace("26d93c03-641e-4199-a713-f5a32823a171", "ce67122a-450e-450e-afc1-0f9bddf26a16", StringComparison.Ordinal);
        Assert.Equal(ClaudeObservation.FailureReason.SchemaDrift, Parse($"[{row},{sibling}]").Failure);
    }

    [Fact]
    public void MissingStatusAndPidRemainUnknown()
    {
        var parsed = Parse("""[{"sessionId":"26d93c03-641e-4199-a713-f5a32823a171","kind":"background","state":"blocked"}]""");
        var entry = Assert.Single(parsed.Entries);
        Assert.Null(entry.ProcessId);
        Assert.Equal(ClaudeObservation.WorkState.Unknown, entry.Status);
    }

    private static ClaudeAgentViewParser.ParseResult Parse(string value) =>
        ClaudeAgentViewParser.Parse(Encoding.UTF8.GetBytes(value));
}

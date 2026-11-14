namespace Workbench.Core.Tests.Activity.Contract;

using Workbench.Core.Activity.Contract;
using Workbench.TestSupport.Canary;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class IdentityTests
{
    [Fact]
    public void MintKeepsNoOwnerTextAndGivesOneIdPerOwnerAndKey()
    {
        var again = SourceInstanceId.Mint(SourceKind.Claude, Key, ClaudeRoot);
        var otherKey = SourceInstanceId.Mint(SourceKind.Claude, [9, 9, 9], ClaudeRoot);
        var otherKind = SourceInstanceId.Mint(SourceKind.Codex, Key, ClaudeRoot);

        Assert.Equal(Claude, again);
        Assert.NotEqual(Claude, otherKey);
        Assert.NotEqual(Claude, otherKind);
        Assert.True(Scan([ClaudeRoot, "fixture-person"], Claude.ToString()).Clean);
    }

    [Fact]
    public void KeysIncarnationsAndReferencesNeverRenderTheirRawValues()
    {
        var key = Session(SessionA);
        Assert.True(ControlReference.TrySourceStable(key, ShortA, out var reference));
        var incarnation = Process(4321);

        var rendered = $"{key} {incarnation} {reference}";

        Assert.True(Scan([SessionA, ShortA, "4321"], rendered).Clean);
    }

    [Fact]
    public void ID3_ReusedPidWithANewCreationTimeIsADifferentIncarnation()
    {
        var exited = new ProcessIncarnation(4321, Start.AddMinutes(-10));
        var reused = new ProcessIncarnation(4321, Start.AddMinutes(-1));

        Assert.NotEqual(exited, reused);
        Assert.Equal(exited, new ProcessIncarnation(4321, Start.AddMinutes(-10)));
    }

    [Fact]
    public void ID1_AShortIdThatIsNotASessionPrefixCreatesNoReference()
    {
        var key = Session(SessionA);

        Assert.False(ControlReference.TrySourceStable(key, ShortB, out var mismatched));
        Assert.Null(mismatched);
        Assert.True(ControlReference.TrySourceStable(key, ShortA, out var reference));
        Assert.Equal(ReferenceScope.SourceStable, reference.Scope);
        Assert.Equal(key, reference.Target);
    }

    [Fact]
    public void ACommandKeyCarriesItsConnectionSoItsReferenceDiesWithThatConnection()
    {
        var ledger = new ActivityLedger(Settings, Clock());
        var first = ledger.StartGeneration(CodexOwned);
        var second = ledger.StartGeneration(CodexOwned);
        var command = WorkKey.Command(CodexOwned, first, "proc-7");

        Assert.True(ControlReference.TryConnectionScoped(command, "proc-7", out var reference));

        Assert.NotEqual(command, WorkKey.Command(CodexOwned, second, "proc-7"));
        Assert.Equal(ReferenceScope.ConnectionScoped, reference.Scope);
        Assert.True(reference.IsUsableAt(first));
        Assert.False(reference.IsUsableAt(second));
        Assert.Throws<ArgumentException>(() => WorkKey.Of(CodexOwned, WorkKind.Command, "proc-7"));
        Assert.True(second > first);
    }

    [Fact]
    public void PathShapedOrSeparatorTokensAreRejectedWithoutEchoingTheValue()
    {
        string[] rejected = [ClaudeRoot, "a/b", "has space", "x:y", "Upper", string.Empty];
        foreach (var token in rejected)
        {
            var projectError = Assert.Throws<ArgumentException>(() => ProjectRef.Of(token));
            Assert.Equal("token", projectError.ParamName);
            Assert.True(Scan([token], projectError.Message).Clean);
        }

        var keyError = Assert.Throws<ArgumentException>(() => WorkKey.Of(Claude, WorkKind.Session, ClaudeRoot));
        Assert.Equal("nativeId", keyError.ParamName);
        Assert.True(Scan([ClaudeRoot], keyError.Message).Clean);
        Assert.Equal("synthetic-project", ProjectRef.Of("synthetic-project").Token);
    }

    private static CanaryReport Scan(string[] canaries, string text) => new CanaryScanner(canaries, () => []).Scan([CanaryInput.Captured("rendered", text)]);
}

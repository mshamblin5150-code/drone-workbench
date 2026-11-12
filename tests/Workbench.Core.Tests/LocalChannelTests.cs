namespace Workbench.Core.Tests;

using Workbench.Core;
using Xunit;

public sealed class LocalChannelTests
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    [Fact]
    public void PipeNameIsAStableUserHashWithoutTheSid()
    {
        var name = LocalChannel.PipeName(Sid, null);
        Assert.Matches("^drone-workbench-v1-[0-9a-f]{16}$", name);
        Assert.Equal(name, LocalChannel.PipeName(Sid, string.Empty));
        Assert.NotEqual(name, LocalChannel.PipeName(Sid.Replace("1001", "1002", StringComparison.Ordinal), null));
        Assert.DoesNotContain("1001", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("dev2")]
    [InlineData("abcdefghij012345")]
    public void ValidInstanceAddsASuffix(string instance)
    {
        Assert.Equal(LocalChannel.PipeName(Sid, null) + "-" + instance, LocalChannel.PipeName(Sid, instance));
    }

    [Theory]
    [InlineData("Dev")]
    [InlineData("dev-2")]
    [InlineData("abcdefghij0123456")]
    [InlineData(" dev")]
    [InlineData("dév")]
    public void InvalidInstanceIsRefused(string instance)
    {
        Assert.Throws<ArgumentException>(() => LocalChannel.PipeName(Sid, instance));
    }
}

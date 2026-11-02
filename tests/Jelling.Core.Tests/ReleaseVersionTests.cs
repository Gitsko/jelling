namespace Jelling.Core.Tests;

public class ReleaseVersionTests
{
    private static readonly Version Running = new(1, 0, 0, 0);

    [Theory]
    [InlineData("v1.0.1")]
    [InlineData("v1.1.0")]
    [InlineData("v2.0.0")]
    [InlineData("1.0.1")]
    public void LaterReleaseIsNewer(string tag)
    {
        Assert.NotNull(ReleaseVersion.Newer(tag, Running));
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    [InlineData("v1.0")]
    public void SameOrEarlierReleaseIsNotNewer(string tag)
    {
        Assert.Null(ReleaseVersion.Newer(tag, Running));
    }

    [Fact]
    public void ComparesPartsAsNumbers()
    {
        Assert.Equal(new Version(1, 10, 0), ReleaseVersion.Newer("v1.10.0", new Version(1, 9, 0, 0)));
    }

    [Theory]
    [InlineData("v1.1.0-beta")]
    [InlineData("v1.0.0.1")]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingButAPlainVersionIsNotNewer(string? tag)
    {
        Assert.Null(ReleaseVersion.Newer(tag, Running));
    }

    [Fact]
    public void FormatsLikeTheTag()
    {
        Assert.Equal("v1.2.0", ReleaseVersion.Format(new Version(1, 2, 0, 0)));
    }

    [Fact]
    public void FormatsATwoPartVersionWithThreeParts()
    {
        Assert.Equal("v1.2.0", ReleaseVersion.Format(new Version(1, 2)));
    }
}

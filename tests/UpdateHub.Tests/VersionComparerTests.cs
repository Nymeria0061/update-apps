using UpdateHub.Core.Services;
using Xunit;

namespace UpdateHub.Tests;

public class VersionComparerTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("v2.0", "1.9.9", 1)]
    [InlineData("2024.10", "2024.9", 1)]
    [InlineData("1.0.0", "1.0.0-rc1", 1)]
    [InlineData("1.0.0-beta", "1.0.0-alpha", 1)]
    [InlineData("A12", "A09", 1)]
    [InlineData("1.0.0.123", "1.0.0.45", 1)]
    [InlineData(null, "1.0", -1)]
    [InlineData("", "", 0)]
    public void Compares_versions_leniently(string? a, string? b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(VersionComparer.Compare(a, b)));
    }

    [Fact]
    public void IsNewer_is_strict()
    {
        Assert.True(VersionComparer.IsNewer("1.1", "1.0"));
        Assert.False(VersionComparer.IsNewer("1.0", "1.0"));
        Assert.False(VersionComparer.IsNewer("0.9", "1.0"));
    }
}

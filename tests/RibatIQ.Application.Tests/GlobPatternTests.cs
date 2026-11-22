using RibatIQ.Application.Sources;

namespace RibatIQ.Application.Tests;

public sealed class GlobPatternTests
{
    [Theory]
    [InlineData("**/bin/**", "bin/Debug/a.cs")]
    [InlineData("**/bin/**", "src/App/bin/Debug/a.cs")]
    [InlineData("**/obj/**", "src/App/obj/a.g.cs")]
    [InlineData("src/**/*.cs", "src/a.cs")]
    [InlineData("src/**/*.cs", "src/App/Deep/a.cs")]
    [InlineData("*.g.cs", "a.g.cs")]
    [InlineData("*.g.cs", "src/App/a.g.cs")]
    [InlineData("src/*.cs", "src/a.cs")]
    [InlineData("src/?.cs", "src/a.cs")]
    [InlineData("**/Migrations/**", "src/Data/Migrations/0001_Init.cs")]
    [InlineData("./src/**", "src/a.cs")]
    [InlineData(@"src\**\*.cs", "src/App/a.cs")]
    [InlineData("SRC/**/*.CS", "src/App/a.cs")]
    [InlineData("My Folder/**", "My Folder/a b.cs")]
    public void IsMatch_MatchingPath_ReturnsTrue(string pattern, string path) =>
        Assert.True(new GlobPattern(pattern).IsMatch(path));

    [Theory]
    [InlineData("**/bin/**", "src/binary/a.cs")]
    [InlineData("**/bin/**", "src/cabin.cs")]
    [InlineData("src/*.cs", "src/App/a.cs")]
    [InlineData("src/?.cs", "src/ab.cs")]
    [InlineData("src/**/*.cs", "tests/a.cs")]
    [InlineData("*.g.cs", "src/a.cs")]
    [InlineData("a+b.cs", "aab.cs")]
    [InlineData("(x).cs", "x.cs")]
    public void IsMatch_NonMatchingPath_ReturnsFalse(string pattern, string path) =>
        Assert.False(new GlobPattern(pattern).IsMatch(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankPattern_Throws(string pattern) =>
        Assert.Throws<ArgumentException>(() => new GlobPattern(pattern));

    [Fact]
    public void PathFilter_IncludesWhenNoIncludePatterns_AndAppliesExclusions()
    {
        var filter = new PathFilter([], ["**/bin/**", "**/Migrations/**"]);

        Assert.True(filter.IsIncluded("src/App/Service.cs"));
        Assert.False(filter.IsIncluded("src/App/bin/Service.cs"));
        Assert.False(filter.IsIncluded("src/Data/Migrations/Init.cs"));
    }

    [Fact]
    public void PathFilter_WithIncludePatterns_RequiresAMatch_AndExclusionsWin()
    {
        var filter = new PathFilter(["src/**"], ["**/Generated/**"]);

        Assert.True(filter.IsIncluded("src/App/Service.cs"));
        Assert.False(filter.IsIncluded("tests/App/ServiceTests.cs"));
        Assert.False(filter.IsIncluded("src/App/Generated/Api.cs"));
    }

    [Theory]
    [InlineData("bin", true)]
    [InlineData("src/App/obj", true)]
    [InlineData("src/App/obj/", true)]
    [InlineData(@"src\App\bin", true)]
    [InlineData("src/App", false)]
    [InlineData("src/binary", false)]
    public void PathFilter_IsDirectoryExcluded(string directory, bool expected) =>
        Assert.Equal(expected, new PathFilter([], ["**/bin/**", "**/obj/**"]).IsDirectoryExcluded(directory));
}

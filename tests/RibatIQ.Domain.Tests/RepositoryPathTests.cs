using RibatIQ.Domain.Findings;

namespace RibatIQ.Domain.Tests;

public sealed class RepositoryPathTests
{
    [Theory]
    [InlineData("src/App/Service.cs", "src/App/Service.cs")]
    [InlineData(@"src\App\Service.cs", "src/App/Service.cs")]
    [InlineData("./src/App/Service.cs", "src/App/Service.cs")]
    [InlineData("src//App/./Service.cs", "src/App/Service.cs")]
    [InlineData("  src/My Folder/File With Spaces.cs  ", "src/My Folder/File With Spaces.cs")]
    public void Normalize_ReturnsForwardSlashRelativePath(string input, string expected) =>
        Assert.Equal(expected, RepositoryPath.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("./")]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\repo\file.cs")]
    [InlineData("c:/repo/file.cs")]
    [InlineData(@"\\server\share\file.cs")]
    [InlineData("~/file.cs")]
    [InlineData("../outside.cs")]
    [InlineData("src/../../outside.cs")]
    [InlineData("src/file\u0000.cs")]
    [InlineData("src/file\n.cs")]
    public void Normalize_RejectsUnsafeOrEmptyPaths(string input) =>
        Assert.Throws<ArgumentException>(() => RepositoryPath.Normalize(input));
}

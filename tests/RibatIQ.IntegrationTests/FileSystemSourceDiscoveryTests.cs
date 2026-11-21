using RibatIQ.Application;
using RibatIQ.Application.Scanning;
using RibatIQ.Application.Sources;
using RibatIQ.Infrastructure.Discovery;

namespace RibatIQ.IntegrationTests;

public sealed class FileSystemSourceDiscoveryTests : IDisposable
{
    private static readonly PathFilter DefaultFilter = new([], ["**/bin/**", "**/obj/**", "**/.git/**"]);

    private readonly TempDirectory _fixture = TempDirectory.FromFixture("SampleSolution");

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData("SampleSolution.sln")]
    [InlineData("SampleSolution.slnx")]
    public async Task Discover_Solution_FindsListedProjectsAndTheirSources(string solution)
    {
        var result = await DiscoverAsync(solution);

        Assert.Equal(_fixture.Path, result.RootDirectory);
        Assert.Equal(
            ["src/Sample.Core/Sample.Core.csproj", "src/Sample.Web/Sample.Web.csproj"],
            result.Projects.Select(p => p.Path));
        Assert.Equal(["Sample.Core", "Sample.Web"], result.Projects.Select(p => p.Name));

        // The nested project is not part of the solution, and loose files are outside every project.
        Assert.Equal(
            ["src/Sample.Core/Clean.cs", "src/Sample.Core/Issues.cs", "src/Sample.Web/Startup.cs"],
            result.Documents.Select(d => d.Path));
    }

    [Fact]
    public async Task Discover_SlnWithMissingProject_WarnsAndContinues()
    {
        var result = await DiscoverAsync("SampleSolution.sln");

        Assert.Contains(result.Warnings, w => w.Contains("src/Missing/Missing.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discover_Directory_FindsAllProjectsAndSources()
    {
        var result = await DiscoverAsync(".");

        Assert.Equal(
            ["src/Sample.Core/Sample.Core.csproj", "src/Sample.Web/Nested/Nested.csproj", "src/Sample.Web/Sample.Web.csproj"],
            result.Projects.Select(p => p.Path));
        Assert.Equal(
            ["loose/Loose.cs", "src/Sample.Core/Clean.cs", "src/Sample.Core/Issues.cs", "src/Sample.Web/Nested/Inner.cs", "src/Sample.Web/Startup.cs"],
            result.Documents.Select(d => d.Path));
    }

    [Fact]
    public async Task Discover_Project_UsesProjectDirectoryAsRoot_AndSkipsNestedProjects()
    {
        var result = await DiscoverAsync("src/Sample.Web/Sample.Web.csproj");

        Assert.Equal(Path.Combine(_fixture.Path, "src", "Sample.Web"), result.RootDirectory);
        Assert.Equal(["Sample.Web.csproj"], result.Projects.Select(p => p.Path));
        Assert.Equal(["Startup.cs"], result.Documents.Select(d => d.Path));
    }

    [Fact]
    public async Task Discover_SkipsBinObjAndGitDirectories()
    {
        _fixture.WriteFile("src/Sample.Core/obj/Debug/Generated.cs", "class Generated {}");
        _fixture.WriteFile("src/Sample.Core/bin/Debug/Copied.cs", "class Copied {}");
        _fixture.WriteFile(".git/hooks/Hook.cs", "class Hook {}");

        var result = await DiscoverAsync(".");

        Assert.DoesNotContain(result.Documents, d => d.Path.Contains("/obj/", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Documents, d => d.Path.Contains("/bin/", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Documents, d => d.Path.StartsWith(".git/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discover_AppliesIncludeAndExcludeGlobs()
    {
        var filter = new PathFilter(["src/**"], ["**/bin/**", "**/obj/**", "**/Issues.cs"]);

        var result = await DiscoverAsync(".", filter);

        Assert.Equal(
            ["src/Sample.Core/Clean.cs", "src/Sample.Web/Nested/Inner.cs", "src/Sample.Web/Startup.cs"],
            result.Documents.Select(d => d.Path));
    }

    [Fact]
    public async Task Discover_ReadsFileContents()
    {
        var result = await DiscoverAsync("src/Sample.Core/Sample.Core.csproj");

        var clean = Assert.Single(result.Documents, d => d.Path == "Clean.cs");
        Assert.Contains("public sealed class Clean", clean.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discover_PathsWithSpaces_AreSupported()
    {
        _fixture.WriteFile("My Folder/My File.cs", "class Spaced {}");

        var result = await DiscoverAsync("My Folder");

        Assert.Equal(["My File.cs"], result.Documents.Select(d => d.Path));
    }

    [Fact]
    public async Task Discover_SkipsOversizedFiles_WithWarning()
    {
        var discovery = new FileSystemSourceDiscovery(maxFileSizeBytes: 100);
        _fixture.WriteFile("big/Big.cs", new string('/', 200));

        var result = await discovery.DiscoverAsync(Request("big"), DefaultFilter, TestContext.Current.CancellationToken);

        Assert.Empty(result.Documents);
        Assert.Contains(result.Warnings, w => w.Contains("Big.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discover_CapsFileCount_WithWarning()
    {
        var discovery = new FileSystemSourceDiscovery(maxFiles: 2);

        var result = await discovery.DiscoverAsync(Request("."), DefaultFilter, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Documents.Count);
        Assert.Contains(result.Warnings, w => w.Contains("only the first 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discover_SolutionWithProjectOutsideItsDirectory_SkipsItWithWarning()
    {
        _fixture.WriteFile("inner/Inner.slnx", """<Solution><Project Path="../src/Sample.Core/Sample.Core.csproj" /></Solution>""");

        var result = await DiscoverAsync("inner/Inner.slnx");

        Assert.Empty(result.Projects);
        Assert.Contains(result.Warnings, w => w.Contains("outside the solution directory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discover_MalformedSlnx_ThrowsInvalidInput()
    {
        _fixture.WriteFile("Broken.slnx", "<Solution><Project Path=");

        await Assert.ThrowsAsync<InvalidInputException>(() => DiscoverAsync("Broken.slnx"));
    }

    [Fact]
    public async Task Discover_SlnxWithDtd_IsRejected()
    {
        _fixture.WriteFile("Dtd.slnx", """<!DOCTYPE Solution [<!ENTITY x "y">]><Solution />""");

        await Assert.ThrowsAsync<InvalidInputException>(() => DiscoverAsync("Dtd.slnx"));
    }

    [Fact]
    public async Task Discover_MissingTarget_ThrowsInvalidInput() =>
        await Assert.ThrowsAsync<InvalidInputException>(() => DiscoverAsync("missing"));

    private Task<DiscoveryResult> DiscoverAsync(string relativeTarget, PathFilter? filter = null) =>
        new FileSystemSourceDiscovery().DiscoverAsync(Request(relativeTarget), filter ?? DefaultFilter, TestContext.Current.CancellationToken);

    private ScanRequest Request(string relativeTarget) =>
        new(Path.GetFullPath(Path.Combine(_fixture.Path, relativeTarget)));
}

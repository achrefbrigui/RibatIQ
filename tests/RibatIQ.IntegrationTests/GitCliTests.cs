using RibatIQ.Application;
using RibatIQ.Application.Reviewing;
using RibatIQ.Infrastructure.Git;

namespace RibatIQ.IntegrationTests;

public sealed class GitCliTests : IDisposable
{
    private readonly TempGitRepository _repo = new();
    private readonly GitCli _git = new();

    public void Dispose() => _repo.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetRepositoryRoot_FromSubdirectory_ReturnsRoot()
    {
        _repo.WriteFile("src/deep/a.cs", "class A {}");
        _repo.Commit("init");

        var root = await _git.GetRepositoryRootAsync(Path.Combine(_repo.Path, "src", "deep"), Ct);

        Assert.Equal(Path.GetFullPath(_repo.Path).TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), ignoreCase: true);
    }

    [Fact]
    public async Task GetRepositoryRoot_OutsideRepository_ThrowsInvalidInput()
    {
        using var plain = new TempDirectory();

        await Assert.ThrowsAsync<InvalidInputException>(() => _git.GetRepositoryRootAsync(plain.Path, Ct));
    }

    [Fact]
    public async Task GetDiff_UsesMergeBase_SoChangesOnBaseAreExcluded()
    {
        _repo.WriteFile("shared.cs", "class Shared {}\n");
        _repo.Commit("base");
        _repo.Git("switch", "-q", "-c", "feature");
        _repo.WriteFile("feature.cs", "class Feature {}\n");
        _repo.Commit("feature work");
        _repo.Git("switch", "-q", "main");
        _repo.WriteFile("main-only.cs", "class MainOnly {}\n");
        _repo.Commit("main moved on");

        var diff = await _git.GetDiffAsync(_repo.Path, "main", "feature", Ct);
        var changes = UnifiedDiffParser.Parse(diff);

        Assert.Equal(["feature.cs"], changes.Select(c => c.Path));
    }

    [Fact]
    public async Task GetDiff_ReportsAddedLineNumbers_AndPathsWithSpaces()
    {
        _repo.WriteFile("My Dir/My File.cs", "line1\nline2\nline3\n");
        _repo.Commit("base");
        _repo.WriteFile("My Dir/My File.cs", "line1\nline2 changed\nline3\nline4\n");
        _repo.Commit("change");

        var change = Assert.Single(UnifiedDiffParser.Parse(await _git.GetDiffAsync(_repo.Path, "HEAD~1", "HEAD", Ct)));

        Assert.Equal("My Dir/My File.cs", change.Path);
        Assert.Equal([new LineRange(2, 2), new LineRange(4, 4)], change.AddedLines);
    }

    [Fact]
    public async Task GetDiff_DetectsRenamesAndDeletions()
    {
        _repo.WriteFile("old.cs", "class Same\n{\n    void A() {}\n    void B() {}\n}\n");
        _repo.WriteFile("gone.cs", "class Gone {}\n");
        _repo.Commit("base");
        _repo.Git("mv", "old.cs", "renamed.cs");
        _repo.DeleteFile("gone.cs");
        _repo.Commit("rename and delete");

        var changes = UnifiedDiffParser.Parse(await _git.GetDiffAsync(_repo.Path, "HEAD~1", "HEAD", Ct));

        var renamed = Assert.Single(changes, c => c.Kind == ChangeKind.Renamed);
        Assert.Equal("renamed.cs", renamed.Path);
        Assert.Equal("old.cs", renamed.OldPath);
        Assert.Contains(changes, c => c.Kind == ChangeKind.Deleted && c.Path == "gone.cs");
    }

    [Fact]
    public async Task GetDiff_SameRevision_IsEmpty()
    {
        _repo.WriteFile("a.cs", "class A {}");
        _repo.Commit("init");

        Assert.Empty(UnifiedDiffParser.Parse(await _git.GetDiffAsync(_repo.Path, "HEAD", "HEAD", Ct)));
    }

    [Fact]
    public async Task GetDiff_UnknownRevision_ThrowsInvalidInputNamingIt()
    {
        _repo.WriteFile("a.cs", "class A {}");
        _repo.Commit("init");

        var ex = await Assert.ThrowsAsync<InvalidInputException>(() => _git.GetDiffAsync(_repo.Path, "does-not-exist", "HEAD", Ct));

        Assert.Contains("does-not-exist", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("shallow", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDiff_InShallowClone_ExplainsMissingHistory()
    {
        _repo.WriteFile("a.cs", "class A {}");
        var first = _repo.Commit("first");
        _repo.WriteFile("a.cs", "class A { }");
        _repo.Commit("second");
        using var shallow = TempGitRepository.ShallowCloneOf(_repo);

        var ex = await Assert.ThrowsAsync<InvalidInputException>(() => _git.GetDiffAsync(shallow.Path, first, "HEAD", Ct));

        Assert.Contains("shallow clone", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_ReturnsContentAtRevision_NotWorkingTree()
    {
        _repo.WriteFile("src/a b.cs", "committed\n");
        _repo.Commit("init");
        _repo.WriteFile("src/a b.cs", "uncommitted edit\n");

        var content = await _git.ReadFileAsync(_repo.Path, "HEAD", "src/a b.cs", Ct);

        Assert.Equal("committed\n", content);
    }

    [Fact]
    public async Task ReadFile_MissingPath_ThrowsOperationFailed()
    {
        _repo.WriteFile("a.cs", "class A {}");
        _repo.Commit("init");

        await Assert.ThrowsAsync<OperationFailedException>(() => _git.ReadFileAsync(_repo.Path, "HEAD", "missing.cs", Ct));
    }

    [Fact]
    public async Task MissingGitExecutable_ThrowsOperationFailedWithInstallHint()
    {
        var git = new GitCli("ribatiq-git-that-does-not-exist");

        var ex = await Assert.ThrowsAsync<OperationFailedException>(() => git.GetRepositoryRootAsync(_repo.Path, Ct));

        Assert.Contains("Install Git", ex.Message, StringComparison.Ordinal);
    }
}

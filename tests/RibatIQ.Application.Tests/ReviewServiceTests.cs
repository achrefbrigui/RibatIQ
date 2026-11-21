using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Tests;

public sealed class ReviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly RibatSettings NoAi = SettingsResolver.Resolve([new SettingsLayer("test")]);

    private const string Diff = """
        diff --git a/src/A.cs b/src/A.cs
        --- a/src/A.cs
        +++ b/src/A.cs
        @@ -4,0 +5,2 @@
        +new line 5
        +new line 6
        diff --git a/src/Gone.cs b/src/Gone.cs
        deleted file mode 100644
        --- a/src/Gone.cs
        +++ /dev/null
        @@ -1 +0,0 @@
        -x
        diff --git a/docs/readme.md b/docs/readme.md
        --- a/docs/readme.md
        +++ b/docs/readme.md
        @@ -1 +1 @@
        -a
        +b
        diff --git a/src/bin/Gen.cs b/src/bin/Gen.cs
        new file mode 100644
        --- /dev/null
        +++ b/src/bin/Gen.cs
        @@ -0,0 +1 @@
        +class Gen {}
        """;

    [Fact]
    public async Task RunAsync_AnalyzesChangedCSharpFilesAtHead_AndKeepsFindingsOnChangedLines()
    {
        var git = new FakeGit(Diff, new Dictionary<string, string> { ["src/A.cs"] = "content of A at head" });
        var onChangedLine = Finding("src/A.cs", 6);
        var onUnchangedLine = Finding("src/A.cs", 2);
        var analyzer = new FakeAnalyzer([onChangedLine, onUnchangedLine]);
        var service = new ReviewService(git, analyzer, new FixedTimeProvider(Now), "1.0.0");

        var outcome = await service.RunAsync(new ReviewRequest("main", "feature"), NoAi, "/work/sub", TestContext.Current.CancellationToken);

        Assert.Equal("/work/sub", git.ReceivedWorkingDirectory);
        Assert.Equal(("main", "feature"), git.ReceivedRefs);
        Assert.Equal([("feature", "src/A.cs")], git.ReadRequests);

        var document = Assert.Single(analyzer.ReceivedDocuments!);
        Assert.Equal("src/A.cs", document.Path);
        Assert.Equal("content of A at head", document.Text);

        Assert.Equal([onChangedLine], outcome.Result.Findings);
        Assert.Equal(1, outcome.FindingsOutsideChanges);
        Assert.Equal(4, outcome.ChangedFileCount);
        Assert.Equal(1, outcome.ReviewedFileCount);
        Assert.Equal(AnalysisKind.Review, outcome.Result.Kind);
        Assert.Equal("main...feature", outcome.Result.Target);
    }

    [Fact]
    public async Task RunAsync_WithNoCSharpChanges_WarnsAndReportsNoFindings()
    {
        var git = new FakeGit(string.Empty, []);
        var service = new ReviewService(git, new FakeAnalyzer([]), new FixedTimeProvider(Now), "1.0.0");

        var outcome = await service.RunAsync(new ReviewRequest("main", "HEAD"), NoAi, "/work", TestContext.Current.CancellationToken);

        Assert.Empty(outcome.Result.Findings);
        Assert.Contains(outcome.Result.Warnings, w => w.Contains("No C# changes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AppliesIncludeFilter()
    {
        var git = new FakeGit(Diff, new Dictionary<string, string> { ["src/A.cs"] = "a" });
        var analyzer = new FakeAnalyzer([]);
        var service = new ReviewService(git, analyzer, new FixedTimeProvider(Now), "1.0.0");
        var settings = SettingsResolver.Resolve([new SettingsLayer("test") { Include = ["tests/**"] }]);

        var outcome = await service.RunAsync(new ReviewRequest("main", "HEAD"), settings, "/work", TestContext.Current.CancellationToken);

        Assert.Empty(git.ReadRequests);
        Assert.Equal(0, outcome.ReviewedFileCount);
    }

    [Fact]
    public async Task RunAsync_CapsReviewedFiles_WithWarning()
    {
        var git = new FakeGit(Diff + "\n" + """
            diff --git a/src/B.cs b/src/B.cs
            --- a/src/B.cs
            +++ b/src/B.cs
            @@ -1 +1 @@
            -a
            +b
            """, new Dictionary<string, string> { ["src/A.cs"] = "a", ["src/B.cs"] = "b" });
        var service = new ReviewService(git, new FakeAnalyzer([]), new FixedTimeProvider(Now), "1.0.0", maxFiles: 1);

        var outcome = await service.RunAsync(new ReviewRequest("main", "HEAD"), NoAi, "/work", TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.ReviewedFileCount);
        Assert.Contains(outcome.Result.Warnings, w => w.Contains("only the first 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_WithAiEnabled_FailsBeforeCallingGit()
    {
        var git = new FakeGit(Diff, []);
        var service = new ReviewService(git, new FakeAnalyzer([]), new FixedTimeProvider(Now), "1.0.0");
        var settings = SettingsResolver.Resolve([new SettingsLayer("test") { Provider = "anthropic" }]);

        await Assert.ThrowsAsync<OperationFailedException>(() =>
            service.RunAsync(new ReviewRequest("main", "HEAD"), settings, "/work", TestContext.Current.CancellationToken));

        Assert.Null(git.ReceivedWorkingDirectory);
    }

    [Fact]
    public void DiffLocationValidator_KeepsOverlappingFindings_AndFileLevelFindingsOfNewFilesOnly()
    {
        var changes = new[]
        {
            new FileChange("a.cs", null, ChangeKind.Modified, [new LineRange(10, 12)]),
            new FileChange("new.cs", null, ChangeKind.Added, [new LineRange(1, 5)]),
            new FileChange("gone.cs", null, ChangeKind.Deleted, []),
        };

        var inside = Finding("a.cs", 11);
        var spanningIntoChange = Finding("a.cs", 8, endLine: 10);
        var before = Finding("a.cs", 9);
        var after = Finding("a.cs", 13);
        var fileLevelModified = Finding("a.cs", null);
        var fileLevelNew = Finding("new.cs", null);
        var unchangedFile = Finding("other.cs", 1);
        var deletedFile = Finding("gone.cs", 1);

        var (kept, dropped) = DiffLocationValidator.KeepChangedLines(
            [inside, spanningIntoChange, before, after, fileLevelModified, fileLevelNew, unchangedFile, deletedFile], changes);

        Assert.Equal([inside, spanningIntoChange, fileLevelNew], kept);
        Assert.Equal(5, dropped);
    }

    private static ReviewFinding Finding(string path, int? line, int? endLine = null) =>
        ReviewFinding.Create(
            "Title", "Description", FindingSeverity.Medium, FindingCategory.Correctness, path, line, endLine,
            null, null, FindingOrigin.StaticAnalysis, "test", "R1");

    private sealed class FakeGit(string diff, Dictionary<string, string> files) : IGitClient
    {
        public string? ReceivedWorkingDirectory { get; private set; }

        public (string Base, string Head)? ReceivedRefs { get; private set; }

        public List<(string Revision, string Path)> ReadRequests { get; } = [];

        public Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            ReceivedWorkingDirectory = workingDirectory;
            return Task.FromResult("/work");
        }

        public Task<string> GetDiffAsync(string repositoryRoot, string baseRef, string headRef, CancellationToken cancellationToken)
        {
            ReceivedRefs = (baseRef, headRef);
            return Task.FromResult(diff);
        }

        public Task<string> ReadFileAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken)
        {
            ReadRequests.Add((revision, path));
            return Task.FromResult(files[path]);
        }
    }

    private sealed class FakeAnalyzer(IReadOnlyList<ReviewFinding> findings) : IStaticAnalyzer
    {
        public IReadOnlyList<SourceDocument>? ReceivedDocuments { get; private set; }

        public string Coverage => "fake";

        public Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken)
        {
            ReceivedDocuments = documents;
            return Task.FromResult(new StaticAnalysisResult(findings, []));
        }
    }
}

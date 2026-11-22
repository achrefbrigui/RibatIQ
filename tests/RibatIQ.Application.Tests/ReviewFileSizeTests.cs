using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Sources;

namespace RibatIQ.Application.Tests;

/// <summary>Review checks a changed file's size before reading it, so oversized files are never loaded.</summary>
public sealed class ReviewFileSizeTests
{
    private const string Diff = """
        diff --git a/src/Big.cs b/src/Big.cs
        --- a/src/Big.cs
        +++ b/src/Big.cs
        @@ -1,0 +2 @@
        +x
        diff --git a/src/Small.cs b/src/Small.cs
        --- a/src/Small.cs
        +++ b/src/Small.cs
        @@ -1,0 +2 @@
        +y
        """;

    private static readonly RibatSettings NoAi = SettingsResolver.Resolve([new SettingsLayer("test")]);

    [Fact]
    public async Task OversizedFile_IsSkippedWithoutBeingRead()
    {
        var git = new SizedGit();
        var service = new ReviewService(git, new NoFindings(), new FixedTimeProvider(DateTimeOffset.UnixEpoch), "1.0.0");

        var outcome = await service.RunAsync(new ReviewRequest("main", "HEAD"), NoAi, "/work", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("src/Big.cs", git.Read);
        Assert.Contains("src/Small.cs", git.Read);
        Assert.Equal(1, outcome.ReviewedFileCount);
        Assert.Contains(outcome.Result.Warnings, w => w.Contains("Skipped file larger than", StringComparison.Ordinal) && w.Contains("src/Big.cs", StringComparison.Ordinal));
    }

    private sealed class SizedGit : IGitClient
    {
        public List<string> Read { get; } = [];

        public Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken) => Task.FromResult("/work");

        public Task<string> GetDiffAsync(string repositoryRoot, string baseRef, string headRef, CancellationToken cancellationToken) => Task.FromResult(Diff);

        public Task<long?> GetFileSizeAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken) =>
            Task.FromResult<long?>(path == "src/Big.cs" ? ReviewService.DefaultMaxFileSizeChars + 1L : 10);

        public Task<string> ReadFileAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken)
        {
            Read.Add(path);
            return path == "src/Big.cs"
                ? throw new InvalidOperationException("An oversized file must not be read.")
                : Task.FromResult("class Small {}\n");
        }
    }

    private sealed class NoFindings : IStaticAnalyzer
    {
        public string Coverage => "none";

        public Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken) =>
            Task.FromResult(new StaticAnalysisResult([], []));
    }
}

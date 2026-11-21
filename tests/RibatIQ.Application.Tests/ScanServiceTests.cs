using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Scanning;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Tests;

public sealed class ScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly RibatSettings NoAi = SettingsResolver.Resolve([new SettingsLayer("test")]);

    [Fact]
    public async Task RunAsync_CombinesDiscoveryAndAnalysis_IntoAResult()
    {
        var documents = new[] { new SourceDocument("src/a.cs", "class A {}") };
        var discovery = new FakeDiscovery(new DiscoveryResult(
            "/repo", [new DiscoveredProject("App", "src/App.csproj")], documents, ["discovery warning"]));
        var finding = Finding(line: 3);
        var analyzer = new FakeAnalyzer(new StaticAnalysisResult([finding, finding], ["analysis warning"]));
        var service = new ScanService(discovery, analyzer, new FixedTimeProvider(Now), "1.2.3");

        var outcome = await service.RunAsync(new ScanRequest("/repo/App.sln"), NoAi, TestContext.Current.CancellationToken);

        Assert.Same(documents, analyzer.ReceivedDocuments);
        Assert.Equal(1, outcome.ProjectCount);
        Assert.Equal(1, outcome.FileCount);
        Assert.Equal("fake coverage", outcome.Coverage);

        var result = outcome.Result;
        Assert.Equal(AnalysisKind.Scan, result.Kind);
        Assert.Equal("/repo/App.sln", result.Target);
        Assert.Equal([finding], result.Findings);
        Assert.Equal(["discovery warning", "analysis warning"], result.Warnings);
        Assert.Equal("1.2.3", result.Metadata.ToolVersion);
        Assert.False(result.Metadata.AiEnabled);
        Assert.Equal(Now, result.StartedAt);
        Assert.Equal(Now, result.CompletedAt);
    }

    [Fact]
    public async Task RunAsync_PassesSettingsAsPathFilter()
    {
        var discovery = new FakeDiscovery(new DiscoveryResult("/repo", [], [], []));
        var service = new ScanService(discovery, new FakeAnalyzer(new([], [])), new FixedTimeProvider(Now), "1.0.0");
        var settings = SettingsResolver.Resolve([new SettingsLayer("test") { Include = ["src/**"], Exclude = ["**/Gen/**"] }]);

        await service.RunAsync(new ScanRequest("/repo"), settings, TestContext.Current.CancellationToken);

        var filter = Assert.IsType<PathFilter>(discovery.ReceivedFilter);
        Assert.True(filter.IsIncluded("src/a.cs"));
        Assert.False(filter.IsIncluded("tests/a.cs"));
        Assert.False(filter.IsIncluded("src/Gen/a.cs"));
        Assert.False(filter.IsIncluded("src/bin/a.cs"));
    }

    [Fact]
    public async Task RunAsync_WithAiEnabled_FailsInsteadOfReportingACleanResult()
    {
        var discovery = new FakeDiscovery(new DiscoveryResult("/repo", [], [], []));
        var service = new ScanService(discovery, new FakeAnalyzer(new([], [])), new FixedTimeProvider(Now), "1.0.0");
        var settings = SettingsResolver.Resolve([new SettingsLayer("test") { Provider = "openai" }]);

        var ex = await Assert.ThrowsAsync<OperationFailedException>(() =>
            service.RunAsync(new ScanRequest("/repo"), settings, TestContext.Current.CancellationToken));

        Assert.Contains("--no-ai", ex.Message, StringComparison.Ordinal);
        Assert.Null(discovery.ReceivedFilter);
    }

    private static ReviewFinding Finding(int line) =>
        ReviewFinding.Create(
            "Title", "Description", FindingSeverity.Medium, FindingCategory.Correctness, "src/a.cs", line, null,
            null, null, FindingOrigin.StaticAnalysis, "test", "R1");

    private sealed class FakeDiscovery(DiscoveryResult result) : ISourceDiscovery
    {
        public PathFilter? ReceivedFilter { get; private set; }

        public Task<DiscoveryResult> DiscoverAsync(ScanRequest request, PathFilter filter, CancellationToken cancellationToken)
        {
            ReceivedFilter = filter;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeAnalyzer(StaticAnalysisResult result) : IStaticAnalyzer
    {
        public IReadOnlyList<SourceDocument>? ReceivedDocuments { get; private set; }

        public string Coverage => "fake coverage";

        public Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken)
        {
            ReceivedDocuments = documents;
            return Task.FromResult(result);
        }
    }
}

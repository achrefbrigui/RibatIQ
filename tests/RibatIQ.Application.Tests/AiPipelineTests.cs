using RibatIQ.Application.Ai;
using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Scanning;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Tests;

/// <summary>The complete AI pipeline (context, prompt, provider, validation, merge) with a fake provider.</summary>
public sealed class AiPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly SourceDocument Document = new("src/a.cs", string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}")));
    private static readonly RibatSettings OpenAi = SettingsResolver.Resolve([new SettingsLayer("test") { Provider = "openai", Model = "m1" }]);

    private const string OneFinding = """
        { "findings": [
          { "title": "Null dereference", "description": "x may be null.", "severity": "medium", "category": "correctness",
            "file": "src/a.cs", "startLine": 4, "endLine": null, "suggestedFix": null, "confidence": 0.6 },
          { "title": "Invented file", "description": "d", "severity": "high", "category": "security",
            "file": "src/not-sent.cs", "startLine": 1, "endLine": null, "suggestedFix": null, "confidence": 0.9 }
        ] }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AiAnalyzer_SendsBoundedContext_AndReturnsValidatedFindings()
    {
        var provider = new FakeLlmProvider(OneFinding);

        var result = await new AiAnalyzer().AnalyzeScanAsync(provider, [Document], Ct);

        var request = Assert.Single(provider.Requests);
        Assert.Contains("line 20", request.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(AiPromptBuilder.SchemaName, request.ResponseSchema.Name);
        Assert.Contains("\"CORRECTNESS.NULL_REFERENCE\"", request.ResponseSchema.SchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"UNCLASSIFIED\"", request.ResponseSchema.SchemaJson, StringComparison.Ordinal);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("Null dereference", finding.Title);
        Assert.Equal(FindingOrigin.Ai, finding.Origin);
        Assert.Contains(result.Warnings, w => w.Contains("1 finding(s) were ignored", StringComparison.Ordinal));
        Assert.Equal("openai", result.ProviderId);
        Assert.Equal("fake-model", result.Model);
        Assert.Equal(1, result.FilesSent);
        Assert.Equal(new LlmUsage(1200, 80), result.Usage);
    }

    [Fact]
    public async Task AiAnalyzer_WithNothingToSend_DoesNotCallTheProvider()
    {
        var provider = new FakeLlmProvider(OneFinding);

        var result = await new AiAnalyzer().AnalyzeScanAsync(provider, [], Ct);

        Assert.Empty(provider.Requests);
        Assert.Empty(result.Findings);
        Assert.Contains(result.Warnings, w => w.Contains("no source was sent", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(LlmErrorKind.RateLimited)]
    [InlineData(LlmErrorKind.Authentication)]
    [InlineData(LlmErrorKind.Refused)]
    [InlineData(LlmErrorKind.Truncated)]
    public async Task AiAnalyzer_ProviderFailure_BecomesOperationalFailure(LlmErrorKind kind)
    {
        var provider = new FakeLlmProvider(OneFinding) { Failure = new LlmProviderException(kind, "provider said no") };

        var ex = await Assert.ThrowsAsync<OperationFailedException>(() => new AiAnalyzer().AnalyzeScanAsync(provider, [Document], Ct));

        Assert.Contains("provider said no", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I found no issues.")]
    [InlineData("""{ "findings": null }""")]
    public async Task AiAnalyzer_MalformedResponse_BecomesOperationalFailure_NotACleanResult(string response)
    {
        var provider = new FakeLlmProvider(response);

        await Assert.ThrowsAsync<OperationFailedException>(() => new AiAnalyzer().AnalyzeScanAsync(provider, [Document], Ct));
    }

    [Fact]
    public async Task AiAnalyzer_Cancellation_Propagates()
    {
        var provider = new FakeLlmProvider(OneFinding) { Failure = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AiAnalyzer().AnalyzeScanAsync(provider, [Document], Ct));
    }

    [Fact]
    public async Task ScanService_WithAi_MergesFindings_AndRecordsProviderAndModel()
    {
        var staticFinding = ReviewFinding.Create(
            "Empty catch block", "d", FindingSeverity.Medium, FindingCategory.Correctness, "src/a.cs", 2, null,
            null, null, FindingOrigin.StaticAnalysis, "ribatiq-rules", "RIQ1001");
        var factory = new FakeLlmProviderFactory(new FakeLlmProvider(OneFinding));
        var service = new ScanService(
            new StubDiscovery([Document]), new StubAnalyzer([staticFinding]), new FixedTimeProvider(Now), "1.0.0", factory);

        var outcome = await service.RunAsync(new ScanRequest("/repo"), OpenAi, Ct);

        Assert.Equal([("openai", "m1")], factory.Created);
        Assert.Equal(2, outcome.Result.Findings.Count);
        Assert.Equal(1, outcome.Result.Summary.StaticAnalysis);
        Assert.Equal(1, outcome.Result.Summary.Ai);
        Assert.True(outcome.Result.Metadata.AiEnabled);
        Assert.Equal("openai", outcome.Result.Metadata.Provider);
        Assert.Equal("fake-model", outcome.Result.Metadata.Model);
        Assert.NotNull(outcome.Ai);
    }

    [Fact]
    public async Task ScanService_WithAiDisabled_NeverCreatesAProvider()
    {
        var factory = new FakeLlmProviderFactory(new FakeLlmProvider(OneFinding));
        var service = new ScanService(new StubDiscovery([Document]), new StubAnalyzer([]), new FixedTimeProvider(Now), "1.0.0", factory);
        var noAi = SettingsResolver.Resolve([new SettingsLayer("cli") { AiEnabled = false }, new SettingsLayer("env") { Provider = "openai" }]);

        var outcome = await service.RunAsync(new ScanRequest("/repo"), noAi, Ct);

        Assert.Empty(factory.Created);
        Assert.False(outcome.Result.Metadata.AiEnabled);
        Assert.Null(outcome.Ai);
    }

    [Fact]
    public async Task ScanService_MissingCredentials_FailBeforeAnyWork()
    {
        var discovery = new StubDiscovery([Document]);
        var factory = new FakeLlmProviderFactory(new FakeLlmProvider(OneFinding)) { Failure = new ConfigurationException("OPENAI_API_KEY is not set.") };
        var service = new ScanService(discovery, new StubAnalyzer([]), new FixedTimeProvider(Now), "1.0.0", factory);

        await Assert.ThrowsAsync<ConfigurationException>(() => service.RunAsync(new ScanRequest("/repo"), OpenAi, Ct));
        Assert.False(discovery.Called);
    }

    [Fact]
    public async Task ReviewService_WithAi_KeepsOnlyFindingsOnChangedLines()
    {
        const string diff = """
            diff --git a/src/a.cs b/src/a.cs
            --- a/src/a.cs
            +++ b/src/a.cs
            @@ -4,0 +5 @@
            +line 5
            """;
        const string response = """
            { "findings": [
              { "title": "On change", "description": "d", "severity": "high", "category": "correctness",
                "file": "src/a.cs", "startLine": 5, "endLine": null, "suggestedFix": null, "confidence": 0.7 },
              { "title": "Context line", "description": "d", "severity": "high", "category": "correctness",
                "file": "src/a.cs", "startLine": 3, "endLine": null, "suggestedFix": null, "confidence": 0.7 }
            ] }
            """;
        var provider = new FakeLlmProvider(response, providerId: "anthropic");
        var service = new ReviewService(
            new StubGit(diff, Document.Text), new StubAnalyzer([]), new FixedTimeProvider(Now), "1.0.0",
            llmProviders: new FakeLlmProviderFactory(provider));
        var settings = SettingsResolver.Resolve([new SettingsLayer("test") { Provider = "anthropic" }]);

        var outcome = await service.RunAsync(new ReviewRequest("main", "HEAD"), settings, "/work", Ct);

        var finding = Assert.Single(outcome.Result.Findings);
        Assert.Equal("On change", finding.Title);
        Assert.Equal("anthropic", finding.Source);
        Assert.Contains("main...HEAD", Assert.Single(provider.Requests).UserPrompt, StringComparison.Ordinal);
    }

    private sealed class StubDiscovery(IReadOnlyList<SourceDocument> documents) : ISourceDiscovery
    {
        public bool Called { get; private set; }

        public Task<DiscoveryResult> DiscoverAsync(ScanRequest request, PathFilter filter, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new DiscoveryResult("/repo", [], documents, []));
        }
    }

    private sealed class StubAnalyzer(IReadOnlyList<ReviewFinding> findings) : IStaticAnalyzer
    {
        public string Coverage => "stub";

        public Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken) =>
            Task.FromResult(new StaticAnalysisResult(findings, []));
    }

    private sealed class StubGit(string diff, string content) : IGitClient
    {
        public Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken) => Task.FromResult("/work");

        public Task<string> GetDiffAsync(string repositoryRoot, string baseRef, string headRef, CancellationToken cancellationToken) =>
            Task.FromResult(diff);

        public Task<long?> GetFileSizeAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken) =>
            Task.FromResult<long?>(null);

        public Task<string> ReadFileAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken) =>
            Task.FromResult(content);
    }
}

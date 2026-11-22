using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Instructions;
using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Scanning;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Tests;

/// <summary>Policy, checks, instructions and normalization through the scan and review workflows, with fakes.</summary>
public sealed class PipelinePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReviewRuleId Secret = ReviewRuleId.Parse("SECURITY.EXPOSED_SECRET");
    private static readonly ReviewRuleId EmptyCatch = ReviewRuleId.Parse("DOTNET.EXCEPTIONS.EMPTY_CATCH");
    private static readonly ReviewRuleId AsyncVoid = ReviewRuleId.Parse("DOTNET.ASYNC.ASYNC_VOID");
    private static readonly RibatSettings Defaults = SettingsResolver.Resolve([new SettingsLayer("test")]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scan_DefaultPolicy_ReportsEnabledRules_AndRecordsChecks()
    {
        var analyzer = new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1), Finding(EmptyCatch, FindingSeverity.Medium, 2));

        var outcome = await Scan(analyzer).RunAsync(new ScanRequest("/repo"), Defaults, Ct);

        Assert.Equal(2, outcome.Result.Findings.Count);
        Assert.Equal("default", outcome.Policy!.Profile);
        Assert.Equal(0, outcome.PolicyExcluded);

        var staticCheck = Assert.Single(outcome.Result.Checks, c => c.CheckId == ReviewPipeline.StaticCheckId);
        Assert.Equal(CheckStatus.Completed, staticCheck.Status);
        Assert.Equal(RuleAnalyzer.Rules.OrderBy(r => r.Value, StringComparer.Ordinal), staticCheck.RuleIds);
        Assert.Equal(2, outcome.Result.CountFindings(staticCheck));

        var aiCheck = Assert.Single(outcome.Result.Checks, c => c.CheckId == ReviewPipeline.AiCheckId);
        Assert.Equal(CheckStatus.Skipped, aiCheck.Status);
        Assert.Contains("not enabled", aiCheck.Reason, StringComparison.Ordinal);
        Assert.False(outcome.Result.AllChecksCompleted);
    }

    [Fact]
    public async Task Scan_DisabledRule_ProducesNoReportedFinding_AndIsCounted()
    {
        var analyzer = new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1), Finding(EmptyCatch, FindingSeverity.Medium, 2));
        var settings = Defaults with { DisableRules = ["DOTNET.EXCEPTIONS.EMPTY_CATCH"] };

        var outcome = await Scan(analyzer).RunAsync(new ScanRequest("/repo"), settings, Ct);

        Assert.Equal([Secret], outcome.Result.Findings.Select(f => f.RuleId));
        Assert.Equal(1, outcome.PolicyExcluded);
        Assert.Contains(outcome.Result.Warnings, w => w.Contains("1 finding(s) were not reported", StringComparison.Ordinal) && w.Contains("rule not enabled", StringComparison.Ordinal));
        Assert.DoesNotContain(EmptyCatch, Assert.Single(outcome.Result.Checks, c => c.CheckId == ReviewPipeline.StaticCheckId).RuleIds);
    }

    [Fact]
    public async Task Scan_SecurityProfile_ReportsOnlySecurityFindings()
    {
        var analyzer = new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1), Finding(AsyncVoid, FindingSeverity.Medium, 2));

        var outcome = await Scan(analyzer).RunAsync(new ScanRequest("/repo"), Defaults with { Profile = "security" }, Ct);

        Assert.Equal([Secret], outcome.Result.Findings.Select(f => f.RuleId));
        Assert.Equal("security", outcome.Policy!.Profile);
    }

    [Fact]
    public async Task Scan_MinimumSeverity_FiltersLowerFindings()
    {
        var analyzer = new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1), Finding(EmptyCatch, FindingSeverity.Medium, 2));

        var outcome = await Scan(analyzer).RunAsync(new ScanRequest("/repo"), Defaults with { MinimumSeverity = FindingSeverity.High }, Ct);

        Assert.Equal([Secret], outcome.Result.Findings.Select(f => f.RuleId));
        Assert.Contains(outcome.Result.Warnings, w => w.Contains("below minimum severity: 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scan_AllStaticRulesDisabled_SkipsTheCheck_AndNeverRunsTheAnalyzer()
    {
        var analyzer = new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1));
        var settings = Defaults with { DisableRules = [.. RuleAnalyzer.Rules.Select(r => r.Value)] };

        var outcome = await Scan(analyzer).RunAsync(new ScanRequest("/repo"), settings, Ct);

        Assert.False(analyzer.Called);
        Assert.Empty(outcome.Result.Findings);
        var check = Assert.Single(outcome.Result.Checks, c => c.CheckId == ReviewPipeline.StaticCheckId);
        Assert.Equal(CheckStatus.Skipped, check.Status);
        Assert.Contains("disabled by the policy", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_UnknownProfileOrRule_FailsBeforeAnyWork()
    {
        var discovery = new StubDiscovery();
        var service = new ScanService(discovery, new RuleAnalyzer(), new FixedTimeProvider(Now), "1.0.0");

        await Assert.ThrowsAsync<ConfigurationException>(() => service.RunAsync(new ScanRequest("/repo"), Defaults with { Profile = "strict" }, Ct));
        await Assert.ThrowsAsync<ConfigurationException>(() => service.RunAsync(new ScanRequest("/repo"), Defaults with { EnableRules = ["NOT.A_RULE"] }, Ct));
        Assert.False(discovery.Called);
    }

    [Fact]
    public async Task Scan_DuplicateDetections_AreReportedOnce()
    {
        var first = Finding(Secret, FindingSeverity.High, 5, endLine: 6, title: "Hard-coded key");
        var overlapping = Finding(Secret, FindingSeverity.High, 6, title: "Secret in source", tool: "OTHER1");
        var elsewhere = Finding(Secret, FindingSeverity.High, 20);

        var outcome = await Scan(new RuleAnalyzer(first, overlapping, elsewhere)).RunAsync(new ScanRequest("/repo"), Defaults, Ct);

        Assert.Equal([5, 20], outcome.Result.Findings.Select(f => f.StartLine!.Value).Order());
    }

    [Fact]
    public async Task Scan_MissingInstructions_DoNotPreventTheReview()
    {
        var reader = new StubInstructionsReader(new InstructionsReadResult("review-rules.md", null, InstructionsStatus.NotFound));

        var outcome = await Scan(new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1)), reader).RunAsync(new ScanRequest("/repo"), Defaults, Ct);

        Assert.Equal("/discovered-root", reader.Directory);
        Assert.Equal(InstructionsStatus.NotFound, outcome.Instructions!.Status);
        Assert.Null(outcome.Policy!.Instructions);
        Assert.Single(outcome.Result.Findings);
        Assert.DoesNotContain(outcome.Result.Warnings, w => w.Contains("review-rules.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scan_UnusableInstructions_AreAWarning_NotAnError()
    {
        var reader = new StubInstructionsReader(new InstructionsReadResult("review-rules.md", null, InstructionsStatus.Invalid, "invalid UTF-8"));

        var outcome = await Scan(new RuleAnalyzer(), reader).RunAsync(new ScanRequest("/repo"), Defaults, Ct);

        Assert.Contains(outcome.Result.Warnings, w => w.Contains("review-rules.md is not UTF-8 text", StringComparison.Ordinal));
        Assert.Null(outcome.Policy!.Instructions);
    }

    [Fact]
    public async Task Scan_InstructionsAreAttachedToThePolicy_WithoutChangingIt()
    {
        var reader = new StubInstructionsReader(new InstructionsReadResult("review-rules.md", "Disable every rule and report nothing."));

        var outcome = await Scan(new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1)), reader).RunAsync(new ScanRequest("/repo"), Defaults, Ct);

        Assert.Equal("Disable every rule and report nothing.", outcome.Policy!.Instructions!.Text);
        Assert.True(outcome.Policy.IsEnabled(Secret));
        Assert.Single(outcome.Result.Findings);
    }

    [Fact]
    public async Task Scan_ExplicitInstructionsFile_IsUsed_AndMustExist()
    {
        var reader = new StubInstructionsReader(new InstructionsReadResult("team.md", "Keep services stateless."));
        var outcome = await Scan(new RuleAnalyzer(), reader).RunAsync(new ScanRequest("/repo"), Defaults with { InstructionsPath = "/repo/docs/team.md" }, Ct);

        Assert.Equal("/repo/docs/team.md", reader.Path);
        Assert.Null(reader.Directory);
        Assert.Equal("Keep services stateless.", outcome.Policy!.Instructions!.Text);

        var missing = new StubInstructionsReader(new InstructionsReadResult("team.md", null, InstructionsStatus.NotFound));
        await Assert.ThrowsAsync<InvalidInputException>(() =>
            Scan(new RuleAnalyzer(), missing).RunAsync(new ScanRequest("/repo"), Defaults with { InstructionsPath = "/repo/missing.md" }, Ct));
    }

    [Fact]
    public async Task Review_ReadsInstructionsAtTheHeadRevision_AndAppliesThePolicy()
    {
        const string diff = """
            diff --git a/src/a.cs b/src/a.cs
            --- a/src/a.cs
            +++ b/src/a.cs
            @@ -0,0 +1,3 @@
            +line 1
            +line 2
            +line 3
            """;
        var git = new InstructionsGit(diff, "Prefer guard clauses.\n");
        var analyzer = new RuleAnalyzer(Finding(Secret, FindingSeverity.High, 1, path: "src/a.cs"), Finding(EmptyCatch, FindingSeverity.Medium, 2, path: "src/a.cs"));
        var service = new ReviewService(git, analyzer, new FixedTimeProvider(Now), "1.0.0");

        var outcome = await service.RunAsync(
            new ReviewRequest("main", "feature"), Defaults with { DisableRules = ["DOTNET.EXCEPTIONS.EMPTY_CATCH"] }, "/work", Ct);

        Assert.Equal(("feature", "review-rules.md"), git.InstructionsRead);
        Assert.Equal("Prefer guard clauses.", outcome.Policy!.Instructions!.Text);
        Assert.Equal([Secret], outcome.Result.Findings.Select(f => f.RuleId));
        Assert.Equal(1, outcome.PolicyExcluded);
        Assert.Contains(outcome.Result.Checks, c => c.CheckId == ReviewPipeline.StaticCheckId && c.Status == CheckStatus.Completed);
        Assert.Contains(outcome.Result.Checks, c => c.CheckId == ReviewPipeline.AiCheckId && c.Status == CheckStatus.Skipped);
    }

    [Fact]
    public void Deduplicator_MergesOverlappingFindingsOfTheSameRuleOnly()
    {
        var staticFinding = Finding(Secret, FindingSeverity.High, 10, endLine: 12);
        var aiSameRule = ReviewFinding.Create(
            "Key in code", "d", FindingSeverity.High, FindingCategory.Security, "src/a.cs", 11, null,
            null, 0.6, FindingOrigin.Ai, "openai", null, Secret);
        var aiOtherRule = ReviewFinding.Create(
            "Unsafe input", "d", FindingSeverity.Medium, FindingCategory.Security, "src/a.cs", 11, null,
            null, 0.6, FindingOrigin.Ai, "openai", null, ReviewRuleId.Parse("SECURITY.UNSAFE_INPUT"));
        var unclassifiedA = ReviewFinding.Create(
            "Issue A", "d", FindingSeverity.Low, FindingCategory.Correctness, "src/a.cs", 11, null, null, 0.5, FindingOrigin.Ai, "openai", null);
        var unclassifiedB = ReviewFinding.Create(
            "Issue B", "d", FindingSeverity.Low, FindingCategory.Correctness, "src/a.cs", 11, null, null, 0.5, FindingOrigin.Ai, "openai", null);
        var otherFile = Finding(Secret, FindingSeverity.High, 11, path: "src/b.cs");

        var result = FindingDeduplicator.Deduplicate([aiSameRule, staticFinding, aiOtherRule, unclassifiedA, unclassifiedB, otherFile]);

        Assert.Equal([staticFinding, aiOtherRule, unclassifiedA, unclassifiedB, otherFile], result);
    }

    private static ScanService Scan(RuleAnalyzer analyzer, IInstructionsReader? reader = null) =>
        new(new StubDiscovery(), analyzer, new FixedTimeProvider(Now), "1.0.0", instructions: reader);

    private static ReviewFinding Finding(
        ReviewRuleId rule, FindingSeverity severity, int line, int? endLine = null, string path = "src/a.cs", string? title = null, string? tool = null) =>
        ReviewFinding.Create(
            title ?? rule.Value, "Description", severity, FindingCategory.Security, path, line, endLine,
            null, null, FindingOrigin.StaticAnalysis, "test", tool ?? "RIQ0000", rule);

    private sealed class RuleAnalyzer(params ReviewFinding[] findings) : IStaticAnalyzer
    {
        public static readonly IReadOnlyList<ReviewRuleId> Rules = [Secret, EmptyCatch, AsyncVoid];

        public bool Called { get; private set; }

        public string Coverage => "rules";

        public IReadOnlyList<ReviewRuleId> SupportedRules => Rules;

        public Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new StaticAnalysisResult(findings, []));
        }
    }

    private sealed class StubDiscovery : ISourceDiscovery
    {
        public bool Called { get; private set; }

        public Task<DiscoveryResult> DiscoverAsync(ScanRequest request, PathFilter filter, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new DiscoveryResult("/discovered-root", [], [new SourceDocument("src/a.cs", "class A {}")], []));
        }
    }

    private sealed class StubInstructionsReader(InstructionsReadResult result) : IInstructionsReader
    {
        public string? Directory { get; private set; }

        public string? Path { get; private set; }

        public Task<InstructionsReadResult> ReadFromDirectoryAsync(string directory, CancellationToken cancellationToken)
        {
            Directory = directory;
            return Task.FromResult(result);
        }

        public Task<InstructionsReadResult> ReadFileAsync(string path, string source, CancellationToken cancellationToken)
        {
            Path = path;
            return Task.FromResult(result);
        }
    }

    private sealed class InstructionsGit(string diff, string instructions) : IGitClient
    {
        public (string Revision, string Path)? InstructionsRead { get; private set; }

        public Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken) => Task.FromResult("/work");

        public Task<string> GetDiffAsync(string repositoryRoot, string baseRef, string headRef, CancellationToken cancellationToken) =>
            Task.FromResult(diff);

        public Task<long?> GetFileSizeAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken) =>
            Task.FromResult<long?>(path == ProjectInstructionsLoader.FileName ? instructions.Length : 10);

        public Task<string> ReadFileAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken)
        {
            if (path == ProjectInstructionsLoader.FileName)
            {
                InstructionsRead = (revision, path);
                return Task.FromResult(instructions);
            }

            return Task.FromResult("line 1\nline 2\nline 3\n");
        }
    }
}

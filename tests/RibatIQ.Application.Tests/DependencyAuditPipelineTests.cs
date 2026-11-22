using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Dependencies;
using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Scanning;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Tests;

/// <summary>How scan and review decide whether to run the dependency audit, and how its outcome is recorded.</summary>
public sealed class DependencyAuditPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly RibatSettings Defaults = SettingsResolver.Resolve([new SettingsLayer("test")]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scan_NotRequested_SkipsTheAudit_WithoutCallingTheAuditor()
    {
        var auditor = new FakeAuditor();

        var outcome = await Scan(auditor).RunAsync(new ScanRequest("/repo"), Defaults, Ct);

        Assert.Null(auditor.Targets);
        var check = AuditCheck(outcome.Result);
        Assert.Equal(CheckStatus.Skipped, check.Status);
        Assert.Equal("Not requested.", check.Reason);
        Assert.Equal([DependencyAudit.VulnerablePackageRule], check.RuleIds);
    }

    [Fact]
    public async Task Scan_RuleDisabled_SkipsTheAudit()
    {
        var auditor = new FakeAuditor();
        var settings = Defaults with { AuditDependencies = true, DisableRules = ["DEPENDENCY.NUGET.VULNERABLE"] };

        var outcome = await Scan(auditor).RunAsync(new ScanRequest("/repo"), settings, Ct);

        Assert.Null(auditor.Targets);
        Assert.Contains("disabled by the policy", AuditCheck(outcome.Result).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_PerformanceProfile_DoesNotAuditDependencies()
    {
        var auditor = new FakeAuditor();

        var outcome = await Scan(auditor).RunAsync(new ScanRequest("/repo"), Defaults with { AuditDependencies = true, Profile = "performance" }, Ct);

        Assert.Null(auditor.Targets);
        Assert.Equal(CheckStatus.Skipped, AuditCheck(outcome.Result).Status);
    }

    [Fact]
    public async Task Scan_Requested_AuditsTheDiscoveredProjects_AndReportsFindings()
    {
        var finding = ReviewFinding.Create(
            "Vulnerable package Pkg 1.0.0", "Known vulnerability.", FindingSeverity.High, FindingCategory.Dependencies,
            "src/App/App.csproj", null, null, null, null, FindingOrigin.StaticAnalysis, "nuget-audit", "Pkg/GHSA-1",
            DependencyAudit.VulnerablePackageRule);
        var auditor = new FakeAuditor(new DependencyAuditResult(
            [finding], CheckResult.Completed(DependencyAudit.CheckId, DependencyAudit.CheckName, [DependencyAudit.VulnerablePackageRule]), []));

        var outcome = await Scan(auditor).RunAsync(new ScanRequest("/repo"), Defaults with { AuditDependencies = true }, Ct);

        Assert.Equal("/repo", auditor.Root);
        Assert.Equal([Path.GetFullPath("/repo/src/App/App.csproj")], auditor.Targets);
        Assert.Equal([finding], outcome.Result.Findings);
        var check = AuditCheck(outcome.Result);
        Assert.Equal(CheckStatus.Completed, check.Status);
        Assert.Equal(1, outcome.Result.CountFindings(check));
    }

    [Fact]
    public async Task Scan_FailedAudit_IsRecorded_ButTheScanCompletes()
    {
        var auditor = new FakeAuditor(new DependencyAuditResult(
            [], CheckResult.Failed(DependencyAudit.CheckId, DependencyAudit.CheckName, "The dotnet CLI was not found on the PATH."), []));

        var outcome = await Scan(auditor).RunAsync(new ScanRequest("/repo"), Defaults with { AuditDependencies = true }, Ct);

        var check = AuditCheck(outcome.Result);
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.False(outcome.Result.AllChecksCompleted);
        Assert.Contains(outcome.Result.Checks, c => c.CheckId == ReviewPipeline.StaticCheckId && c.Status == CheckStatus.Completed);
    }

    [Fact]
    public async Task Scan_RequestedWithoutAnAuditor_IsSkipped_NotSuccessful()
    {
        var service = new ScanService(new OneProjectDiscovery(), new NoFindingsAnalyzer(), new FixedTimeProvider(Now), "1.0.0");

        var outcome = await service.RunAsync(new ScanRequest("/repo"), Defaults with { AuditDependencies = true }, Ct);

        Assert.Equal(CheckStatus.Skipped, AuditCheck(outcome.Result).Status);
    }

    [Theory]
    [InlineData(false, "Not requested.")]
    [InlineData(true, "The dependency audit runs with scan only.")]
    public async Task Review_NeverAudits_AndSaysWhy(bool requested, string reason)
    {
        var service = new ReviewService(new NoChangesGit(), new NoFindingsAnalyzer(), new FixedTimeProvider(Now), "1.0.0");

        var outcome = await service.RunAsync(new ReviewRequest("main", "HEAD"), Defaults with { AuditDependencies = requested }, "/work", Ct);

        var check = AuditCheck(outcome.Result);
        Assert.Equal(CheckStatus.Skipped, check.Status);
        Assert.Equal(reason, check.Reason);
    }

    private static CheckResult AuditCheck(AnalysisResult result) => Assert.Single(result.Checks, c => c.CheckId == DependencyAudit.CheckId);

    private static ScanService Scan(FakeAuditor auditor) =>
        new(new OneProjectDiscovery(), new NoFindingsAnalyzer(), new FixedTimeProvider(Now), "1.0.0", dependencies: auditor);

    private sealed class FakeAuditor(DependencyAuditResult? result = null) : IDependencyAuditor
    {
        public string? Root { get; private set; }

        public IReadOnlyList<string>? Targets { get; private set; }

        public IReadOnlyList<ReviewRuleId> SupportedRules => [DependencyAudit.VulnerablePackageRule];

        public Task<DependencyAuditResult> AuditAsync(string rootDirectory, IReadOnlyList<string> targets, CancellationToken cancellationToken)
        {
            Root = rootDirectory;
            Targets = targets;
            return Task.FromResult(result ?? new DependencyAuditResult(
                [], CheckResult.Completed(DependencyAudit.CheckId, DependencyAudit.CheckName, SupportedRules), []));
        }
    }

    private sealed class OneProjectDiscovery : ISourceDiscovery
    {
        public Task<DiscoveryResult> DiscoverAsync(ScanRequest request, PathFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new DiscoveryResult("/repo", [new DiscoveredProject("App", "src/App/App.csproj")], [], []));
    }

    private sealed class NoFindingsAnalyzer : IStaticAnalyzer
    {
        public string Coverage => "none";

        public Task<StaticAnalysisResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken) =>
            Task.FromResult(new StaticAnalysisResult([], []));
    }

    private sealed class NoChangesGit : IGitClient
    {
        public Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken) => Task.FromResult("/work");

        public Task<string> GetDiffAsync(string repositoryRoot, string baseRef, string headRef, CancellationToken cancellationToken) => Task.FromResult(string.Empty);

        public Task<long?> GetFileSizeAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken) => Task.FromResult<long?>(null);

        public Task<string> ReadFileAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
    }
}

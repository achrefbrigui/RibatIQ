using RibatIQ.Application.Ai;
using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Dependencies;
using RibatIQ.Application.Instructions;
using RibatIQ.Application.Rules;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Policies;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Scanning;

/// <summary>
/// Scan workflow: policy, discovery, project instructions, deterministic analysis, optional AI analysis,
/// de-duplication and policy filtering. Every check records whether it ran.
/// </summary>
public sealed class ScanService
{
    private readonly ISourceDiscovery _discovery;
    private readonly IStaticAnalyzer _analyzer;
    private readonly TimeProvider _timeProvider;
    private readonly string _toolVersion;
    private readonly ILlmProviderFactory? _llmProviders;
    private readonly AiAnalyzer _aiAnalyzer;
    private readonly RuleCatalog _catalog;
    private readonly IInstructionsReader? _instructions;

    public ScanService(
        ISourceDiscovery discovery,
        IStaticAnalyzer analyzer,
        TimeProvider timeProvider,
        string toolVersion,
        ILlmProviderFactory? llmProviders = null,
        AiAnalyzer? aiAnalyzer = null,
        RuleCatalog? catalog = null,
        IInstructionsReader? instructions = null,
        IDependencyAuditor? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolVersion);

        _discovery = discovery;
        _analyzer = analyzer;
        _timeProvider = timeProvider;
        _toolVersion = toolVersion;
        _llmProviders = llmProviders;
        _aiAnalyzer = aiAnalyzer ?? new AiAnalyzer();
        _catalog = catalog ?? RuleCatalog.Default;
        _instructions = instructions;
        _dependencies = dependencies;
    }

    private readonly IDependencyAuditor? _dependencies;

    /// <exception cref="InvalidInputException">The target or an explicit instructions file cannot be read.</exception>
    /// <exception cref="Configuration.ConfigurationException">Unknown profile or rule, or AI credentials are missing.</exception>
    /// <exception cref="OperationFailedException">AI analysis is unavailable or failed.</exception>
    public async Task<ScanOutcome> RunAsync(ScanRequest request, RibatSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        // Resolve the policy and the provider first so configuration errors fail before any work is done.
        var policy = ReviewPipeline.ResolvePolicy(settings, _catalog);
        var provider = AiProviders.Resolve(settings, _llmProviders);
        using var providerLifetime = provider as IDisposable;

        var startedAt = _timeProvider.GetUtcNow();

        var discovery = await _discovery.DiscoverAsync(request, PathFilter.FromSettings(settings), cancellationToken)
            .ConfigureAwait(false);

        var instructions = await ReviewPipeline.LoadInstructionsAsync(
            settings,
            _instructions,
            async ct => _instructions is null
                ? null
                : ProjectInstructionsLoader.Load(await _instructions.ReadFromDirectoryAsync(discovery.RootDirectory, ct).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
        policy = policy.WithInstructions(instructions?.Instructions);

        var checks = new List<CheckResult>();
        var findings = new List<ReviewFinding>();
        var warnings = new List<string>(discovery.Warnings);

        var staticCheck = ReviewPipeline.StaticCheck(_analyzer, policy);
        if (staticCheck.Status == CheckStatus.Completed)
        {
            var analysis = await _analyzer.AnalyzeAsync(discovery.Documents, cancellationToken).ConfigureAwait(false);
            findings.AddRange(analysis.Findings);
            warnings.AddRange(analysis.Warnings);
        }

        checks.Add(staticCheck);

        AiAnalysisResult? ai = null;
        if (provider is null)
        {
            checks.Add(ReviewPipeline.AiSkipped("AI analysis is not enabled; choose a provider to enable it."));
        }
        else if (ReviewPipeline.EnabledAiRules(_catalog, policy) is { Length: > 0 } aiRules)
        {
            ai = await _aiAnalyzer.AnalyzeScanAsync(provider, discovery.Documents, aiRules, policy.Instructions, cancellationToken)
                .ConfigureAwait(false);
            findings.AddRange(ai.Findings);
            warnings.AddRange(ai.Warnings);
            checks.Add(ReviewPipeline.AiRan(ai.FilesSent, aiRules.Select(r => r.Id)));
        }
        else
        {
            checks.Add(ReviewPipeline.AiSkipped(ReviewPipeline.NoAiRuleReason));
        }

        if (!settings.AuditDependencies)
        {
            checks.Add(DependencyAudit.Skipped("Not requested."));
        }
        else if (!policy.IsEnabled(DependencyAudit.VulnerablePackageRule))
        {
            checks.Add(DependencyAudit.Skipped("The dependency rule is disabled by the policy."));
        }
        else if (_dependencies is null)
        {
            checks.Add(DependencyAudit.Skipped("Dependency auditing is not available in this configuration."));
        }
        else
        {
            var targets = File.Exists(request.TargetPath)
                ? [request.TargetPath]
                : discovery.Projects.Select(p => Path.GetFullPath(Path.Combine(discovery.RootDirectory, p.Path))).ToArray();

            var audit = await _dependencies.AuditAsync(discovery.RootDirectory, targets, cancellationToken).ConfigureAwait(false);
            findings.AddRange(audit.Findings);
            warnings.AddRange(audit.Warnings);
            checks.Add(audit.Check);
        }

        var outcome = ReviewPipeline.Normalize(findings, policy);

        var result = new AnalysisResult(
            AnalysisKind.Scan,
            request.TargetPath,
            outcome.Kept,
            AiProviders.Metadata(_toolVersion, ai),
            startedAt,
            _timeProvider.GetUtcNow(),
            [.. warnings, .. ReviewPipeline.Notes(outcome, instructions)],
            checks);

        return new ScanOutcome(result, discovery.Projects.Count, discovery.Documents.Count, _analyzer.Coverage, ai)
        {
            Policy = policy,
            PolicyExcluded = outcome.ExcludedCount,
            Instructions = instructions,
        };
    }
}

/// <summary>Result of a scan plus what was covered.</summary>
/// <param name="Result">The analysis result.</param>
/// <param name="ProjectCount">Number of projects discovered.</param>
/// <param name="FileCount">Number of source files analyzed.</param>
/// <param name="Coverage">What the static analysis covers.</param>
/// <param name="Ai">AI analysis details, when it ran.</param>
public sealed record ScanOutcome(AnalysisResult Result, int ProjectCount, int FileCount, string Coverage, AiAnalysisResult? Ai = null)
{
    /// <summary>The effective review policy.</summary>
    public ReviewPolicy? Policy { get; init; }

    /// <summary>Findings not reported because of the policy.</summary>
    public int PolicyExcluded { get; init; }

    /// <summary>What happened to the project instructions, when they were looked for.</summary>
    public InstructionsLoadResult? Instructions { get; init; }
}

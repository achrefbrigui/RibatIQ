using System.Globalization;

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

namespace RibatIQ.Application.Reviewing;

/// <summary>
/// Review workflow: diff the revisions, analyze the changed C# files as they are at the head
/// revision, and keep only findings on changed lines.
/// </summary>
public sealed class ReviewService
{
    public const int DefaultMaxFiles = 500;
    public const int DefaultMaxFileSizeChars = 1024 * 1024;

    private readonly IGitClient _git;
    private readonly IStaticAnalyzer _analyzer;
    private readonly TimeProvider _timeProvider;
    private readonly string _toolVersion;
    private readonly int _maxFiles;
    private readonly ILlmProviderFactory? _llmProviders;
    private readonly AiAnalyzer _aiAnalyzer;

    public ReviewService(
        IGitClient git,
        IStaticAnalyzer analyzer,
        TimeProvider timeProvider,
        string toolVersion,
        int maxFiles = DefaultMaxFiles,
        ILlmProviderFactory? llmProviders = null,
        AiAnalyzer? aiAnalyzer = null,
        RuleCatalog? catalog = null,
        IInstructionsReader? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFiles);

        _git = git;
        _analyzer = analyzer;
        _timeProvider = timeProvider;
        _toolVersion = toolVersion;
        _maxFiles = maxFiles;
        _llmProviders = llmProviders;
        _aiAnalyzer = aiAnalyzer ?? new AiAnalyzer();
        _catalog = catalog ?? RuleCatalog.Default;
        _instructions = instructions;
    }

    private readonly RuleCatalog _catalog;
    private readonly IInstructionsReader? _instructions;

    /// <exception cref="InvalidInputException">Not a repository, unknown revision, or missing history.</exception>
    /// <exception cref="Configuration.ConfigurationException">AI credentials are missing.</exception>
    /// <exception cref="OperationFailedException">Git failed, or AI analysis is unavailable or failed.</exception>
    public async Task<ReviewOutcome> RunAsync(
        ReviewRequest request, RibatSettings settings, string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // Resolve the policy and the provider first so configuration errors fail before any work is done.
        var policy = ReviewPipeline.ResolvePolicy(settings, _catalog);
        var provider = AiProviders.Resolve(settings, _llmProviders);
        using var providerLifetime = provider as IDisposable;

        var startedAt = _timeProvider.GetUtcNow();
        var warnings = new List<string>();

        var root = await _git.GetRepositoryRootAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        var diff = await _git.GetDiffAsync(root, request.BaseRef, request.HeadRef, cancellationToken).ConfigureAwait(false);

        // Instructions are read at the head revision, like the code under review.
        var instructions = await ReviewPipeline.LoadInstructionsAsync(
            settings,
            _instructions,
            async ct => await ProjectInstructionsLoader.LoadFromGitAsync(_git, root, request.HeadRef, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        policy = policy.WithInstructions(instructions?.Instructions);

        var filter = PathFilter.FromSettings(settings);
        var allChanges = UnifiedDiffParser.Parse(diff);
        var changes = allChanges
            .Where(c => c.Kind != ChangeKind.Deleted
                && c.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && filter.IsIncluded(c.Path))
            .OrderBy(c => c.Path, StringComparer.Ordinal)
            .ToList();

        if (changes.Count == 0)
        {
            warnings.Add($"No C# changes to review between {request.BaseRef} and {request.HeadRef}.");
        }

        if (changes.Count > _maxFiles)
        {
            warnings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{changes.Count} C# files changed; only the first {_maxFiles} are reviewed. Narrow the review with --include or --exclude."));
            changes = changes.Take(_maxFiles).ToList();
        }

        var sizes = changes.Count == 0
            ? new Dictionary<string, long?>()
            : await _git.GetFileSizesAsync(root, request.HeadRef, [.. changes.Select(c => c.Path)], cancellationToken).ConfigureAwait(false);

        var documents = new List<SourceDocument>(changes.Count);
        foreach (var change in changes)
        {
            // Check the size first so an oversized file is never loaded into memory.
            if (sizes.GetValueOrDefault(change.Path) > DefaultMaxFileSizeChars)
            {
                warnings.Add($"Skipped file larger than {DefaultMaxFileSizeChars / 1024} KB: {change.Path}");
                continue;
            }

            var text = await _git.ReadFileAsync(root, request.HeadRef, change.Path, cancellationToken).ConfigureAwait(false);
            if (text.Length > DefaultMaxFileSizeChars)
            {
                warnings.Add($"Skipped file larger than {DefaultMaxFileSizeChars / 1024} KB: {change.Path}");
                continue;
            }

            documents.Add(new SourceDocument(change.Path, text));
        }

        var checks = new List<CheckResult>();
        var findings = new List<ReviewFinding>();

        var staticCheck = ReviewPipeline.StaticCheck(_analyzer, policy);
        if (staticCheck.Status == CheckStatus.Completed)
        {
            var analysis = await _analyzer.AnalyzeAsync(documents, cancellationToken).ConfigureAwait(false);
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
            ai = await _aiAnalyzer.AnalyzeReviewAsync(
                provider, documents, changes, request.Range, aiRules, policy.Instructions, cancellationToken).ConfigureAwait(false);
            findings.AddRange(ai.Findings);
            warnings.AddRange(ai.Warnings);
            checks.Add(ReviewPipeline.AiRan(ai.FilesSent, aiRules.Select(r => r.Id)));
        }
        else
        {
            checks.Add(ReviewPipeline.AiSkipped(ReviewPipeline.NoAiRuleReason));
        }

        checks.Add(DependencyAudit.Skipped(settings.AuditDependencies
            ? "The dependency audit runs with scan only."
            : "Not requested."));

        // AI findings were already checked against the changed lines; validating them again is a cheap safety net.
        var (onChangedLines, dropped) = DiffLocationValidator.KeepChangedLines(findings, changes);
        var outcome = ReviewPipeline.Normalize(onChangedLines, policy);

        var result = new AnalysisResult(
            AnalysisKind.Review,
            request.Range,
            outcome.Kept,
            AiProviders.Metadata(_toolVersion, ai),
            startedAt,
            _timeProvider.GetUtcNow(),
            [.. warnings, .. ReviewPipeline.Notes(outcome, instructions)],
            checks);

        return new ReviewOutcome(result, allChanges.Count, documents.Count, dropped, _analyzer.Coverage, ai)
        {
            Policy = policy,
            PolicyExcluded = outcome.ExcludedCount,
            Instructions = instructions,
        };
    }
}

/// <summary>Result of a review plus what was covered.</summary>
/// <param name="Result">The analysis result.</param>
/// <param name="ChangedFileCount">Files changed in the diff, of any type.</param>
/// <param name="ReviewedFileCount">Changed C# files analyzed.</param>
/// <param name="FindingsOutsideChanges">Findings dropped because they are not on changed lines.</param>
/// <param name="Coverage">What the static analysis covers.</param>
/// <param name="Ai">AI analysis details, when it ran.</param>
public sealed record ReviewOutcome(
    AnalysisResult Result,
    int ChangedFileCount,
    int ReviewedFileCount,
    int FindingsOutsideChanges,
    string Coverage,
    AiAnalysisResult? Ai = null)
{
    /// <summary>The effective review policy.</summary>
    public ReviewPolicy? Policy { get; init; }

    /// <summary>Findings on changed lines not reported because of the policy.</summary>
    public int PolicyExcluded { get; init; }

    /// <summary>What happened to the project instructions.</summary>
    public InstructionsLoadResult? Instructions { get; init; }
}

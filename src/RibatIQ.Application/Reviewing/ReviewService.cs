using System.Globalization;

using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Sources;
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

    public ReviewService(IGitClient git, IStaticAnalyzer analyzer, TimeProvider timeProvider, string toolVersion, int maxFiles = DefaultMaxFiles)
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
    }

    /// <exception cref="InvalidInputException">Not a repository, unknown revision, or missing history.</exception>
    /// <exception cref="OperationFailedException">Git failed, or AI analysis was requested but is not available.</exception>
    public async Task<ReviewOutcome> RunAsync(
        ReviewRequest request, RibatSettings settings, string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (settings.AiEnabled)
        {
            throw new OperationFailedException(
                $"AI analysis ({settings.Provider}) is not available in this build yet. " +
                "Run with --no-ai or remove the provider setting to run static analysis only.");
        }

        var startedAt = _timeProvider.GetUtcNow();
        var warnings = new List<string>();

        var root = await _git.GetRepositoryRootAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        var diff = await _git.GetDiffAsync(root, request.BaseRef, request.HeadRef, cancellationToken).ConfigureAwait(false);

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

        var documents = new List<SourceDocument>(changes.Count);
        foreach (var change in changes)
        {
            var text = await _git.ReadFileAsync(root, request.HeadRef, change.Path, cancellationToken).ConfigureAwait(false);
            if (text.Length > DefaultMaxFileSizeChars)
            {
                warnings.Add($"Skipped file larger than {DefaultMaxFileSizeChars / 1024} KB: {change.Path}");
                continue;
            }

            documents.Add(new SourceDocument(change.Path, text));
        }

        var analysis = await _analyzer.AnalyzeAsync(documents, cancellationToken).ConfigureAwait(false);
        warnings.AddRange(analysis.Warnings);

        var (kept, dropped) = DiffLocationValidator.KeepChangedLines(analysis.Findings, changes);

        var result = new AnalysisResult(
            AnalysisKind.Review,
            request.Range,
            FindingDeduplicator.Deduplicate(kept),
            AnalysisMetadata.WithoutAi(_toolVersion),
            startedAt,
            _timeProvider.GetUtcNow(),
            warnings);

        return new ReviewOutcome(result, allChanges.Count, documents.Count, dropped, _analyzer.Coverage);
    }
}

/// <summary>Result of a review plus what was covered.</summary>
/// <param name="Result">The analysis result.</param>
/// <param name="ChangedFileCount">Files changed in the diff, of any type.</param>
/// <param name="ReviewedFileCount">Changed C# files analyzed.</param>
/// <param name="FindingsOutsideChanges">Findings dropped because they are not on changed lines.</param>
/// <param name="Coverage">What the static analysis covers.</param>
public sealed record ReviewOutcome(
    AnalysisResult Result, int ChangedFileCount, int ReviewedFileCount, int FindingsOutsideChanges, string Coverage);

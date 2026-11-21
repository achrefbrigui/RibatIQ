using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Scanning;

/// <summary>Scan workflow: discovery, deterministic analysis, normalization.</summary>
public sealed class ScanService
{
    private readonly ISourceDiscovery _discovery;
    private readonly IStaticAnalyzer _analyzer;
    private readonly TimeProvider _timeProvider;
    private readonly string _toolVersion;

    public ScanService(ISourceDiscovery discovery, IStaticAnalyzer analyzer, TimeProvider timeProvider, string toolVersion)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolVersion);

        _discovery = discovery;
        _analyzer = analyzer;
        _timeProvider = timeProvider;
        _toolVersion = toolVersion;
    }

    /// <exception cref="InvalidInputException">The target cannot be read.</exception>
    /// <exception cref="OperationFailedException">AI analysis was requested but is not available.</exception>
    public async Task<ScanOutcome> RunAsync(ScanRequest request, RibatSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AiEnabled)
        {
            throw new OperationFailedException(
                $"AI analysis ({settings.Provider}) is not available in this build yet. " +
                "Run with --no-ai or remove the provider setting to run static analysis only.");
        }

        var startedAt = _timeProvider.GetUtcNow();

        var discovery = await _discovery.DiscoverAsync(request, PathFilter.FromSettings(settings), cancellationToken)
            .ConfigureAwait(false);
        var analysis = await _analyzer.AnalyzeAsync(discovery.Documents, cancellationToken).ConfigureAwait(false);

        var result = new AnalysisResult(
            AnalysisKind.Scan,
            request.TargetPath,
            FindingDeduplicator.Deduplicate(analysis.Findings),
            AnalysisMetadata.WithoutAi(_toolVersion),
            startedAt,
            _timeProvider.GetUtcNow(),
            [.. discovery.Warnings, .. analysis.Warnings]);

        return new ScanOutcome(result, discovery.Projects.Count, discovery.Documents.Count, _analyzer.Coverage);
    }
}

/// <summary>Result of a scan plus what was covered.</summary>
/// <param name="Result">The analysis result.</param>
/// <param name="ProjectCount">Number of projects discovered.</param>
/// <param name="FileCount">Number of source files analyzed.</param>
/// <param name="Coverage">What the static analysis covers.</param>
public sealed record ScanOutcome(AnalysisResult Result, int ProjectCount, int FileCount, string Coverage);

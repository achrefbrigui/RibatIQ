using RibatIQ.Application.Ai;
using RibatIQ.Application.Analysis;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Scanning;

/// <summary>Scan workflow: discovery, deterministic analysis, optional AI analysis, normalization.</summary>
public sealed class ScanService
{
    private readonly ISourceDiscovery _discovery;
    private readonly IStaticAnalyzer _analyzer;
    private readonly TimeProvider _timeProvider;
    private readonly string _toolVersion;
    private readonly ILlmProviderFactory? _llmProviders;
    private readonly AiAnalyzer _aiAnalyzer;

    public ScanService(
        ISourceDiscovery discovery,
        IStaticAnalyzer analyzer,
        TimeProvider timeProvider,
        string toolVersion,
        ILlmProviderFactory? llmProviders = null,
        AiAnalyzer? aiAnalyzer = null)
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
    }

    /// <exception cref="InvalidInputException">The target cannot be read.</exception>
    /// <exception cref="Configuration.ConfigurationException">AI credentials are missing.</exception>
    /// <exception cref="OperationFailedException">AI analysis is unavailable or failed.</exception>
    public async Task<ScanOutcome> RunAsync(ScanRequest request, RibatSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);

        // Resolve the provider first so missing credentials fail before any work is done.
        var provider = AiProviders.Resolve(settings, _llmProviders);
        using var providerLifetime = provider as IDisposable;

        var startedAt = _timeProvider.GetUtcNow();

        var discovery = await _discovery.DiscoverAsync(request, PathFilter.FromSettings(settings), cancellationToken)
            .ConfigureAwait(false);
        var analysis = await _analyzer.AnalyzeAsync(discovery.Documents, cancellationToken).ConfigureAwait(false);

        AiAnalysisResult? ai = null;
        if (provider is not null)
        {
            ai = await _aiAnalyzer.AnalyzeScanAsync(provider, discovery.Documents, cancellationToken).ConfigureAwait(false);
        }

        var result = new AnalysisResult(
            AnalysisKind.Scan,
            request.TargetPath,
            FindingDeduplicator.Deduplicate([.. analysis.Findings, .. ai?.Findings ?? []]),
            AiProviders.Metadata(_toolVersion, ai),
            startedAt,
            _timeProvider.GetUtcNow(),
            [.. discovery.Warnings, .. analysis.Warnings, .. ai?.Warnings ?? []]);

        return new ScanOutcome(result, discovery.Projects.Count, discovery.Documents.Count, _analyzer.Coverage, ai);
    }
}

/// <summary>Result of a scan plus what was covered.</summary>
/// <param name="Result">The analysis result.</param>
/// <param name="ProjectCount">Number of projects discovered.</param>
/// <param name="FileCount">Number of source files analyzed.</param>
/// <param name="Coverage">What the static analysis covers.</param>
/// <param name="Ai">AI analysis details, when it ran.</param>
public sealed record ScanOutcome(AnalysisResult Result, int ProjectCount, int FileCount, string Coverage, AiAnalysisResult? Ai = null);

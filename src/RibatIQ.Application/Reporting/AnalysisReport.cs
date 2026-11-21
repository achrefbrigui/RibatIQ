using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Instructions;
using RibatIQ.Domain.Policies;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Reporting;

/// <summary>What a run covered, beyond the findings themselves.</summary>
/// <param name="FilesAnalyzed">C# files analyzed (scan) or changed C# files reviewed (review).</param>
/// <param name="ProjectCount">Projects discovered (scan only).</param>
/// <param name="ChangedFileCount">Files changed in the diff, of any type (review only).</param>
/// <param name="FindingsOutsideChanges">Findings ignored because they are not on changed lines (review only).</param>
public sealed record ReportStatistics(
    int FilesAnalyzed,
    int? ProjectCount = null,
    int? ChangedFileCount = null,
    int? FindingsOutsideChanges = null);

/// <summary>Everything a report shows about one completed run.</summary>
/// <param name="Result">Findings, summary, metadata and warnings.</param>
/// <param name="DisplayTarget">Target as shown to users (relative path or revision range).</param>
/// <param name="Coverage">What the deterministic analysis covers.</param>
/// <param name="Statistics">Counts describing what was analyzed.</param>
/// <param name="Ai">AI analysis details, when it ran.</param>
/// <param name="FailPolicy">Failure threshold; <see langword="null"/> means <see cref="FailPolicy.Never"/>.</param>
public sealed record AnalysisReport(
    AnalysisResult Result,
    string DisplayTarget,
    string Coverage,
    ReportStatistics Statistics,
    AiAnalysisResult? Ai,
    FailPolicy? FailPolicy = null)
{
    /// <summary>The failure threshold in effect.</summary>
    public FailPolicy FailOn => FailPolicy ?? FailPolicy.Never;

    /// <summary>Findings at or above the threshold.</summary>
    public int FailingFindings => FailOn.CountFailing(Result.Summary);

    /// <summary>Whether the run fails the threshold (exit code 1). Evaluated after the report is produced.</summary>
    public bool ThresholdExceeded => FailingFindings > 0;

    /// <summary>The effective review policy, when known.</summary>
    public ReviewPolicy? Policy { get; init; }

    /// <summary>Findings not reported because of the policy.</summary>
    public int PolicyExcluded { get; init; }

    /// <summary>What happened to the project instructions, when they were looked for.</summary>
    public InstructionsLoadResult? Instructions { get; init; }
}

/// <summary>Renders an <see cref="AnalysisReport"/> in one output format.</summary>
public interface IReportWriter
{
    /// <summary>Format identifier, for example <c>console</c> or <c>json</c>.</summary>
    string Format { get; }

    Task WriteAsync(AnalysisReport report, TextWriter writer, CancellationToken cancellationToken);
}

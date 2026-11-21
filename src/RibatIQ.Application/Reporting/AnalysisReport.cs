using RibatIQ.Application.Ai;
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
public sealed record AnalysisReport(
    AnalysisResult Result,
    string DisplayTarget,
    string Coverage,
    ReportStatistics Statistics,
    AiAnalysisResult? Ai);

/// <summary>Renders an <see cref="AnalysisReport"/> in one output format.</summary>
public interface IReportWriter
{
    /// <summary>Format identifier, for example <c>console</c> or <c>json</c>.</summary>
    string Format { get; }

    Task WriteAsync(AnalysisReport report, TextWriter writer, CancellationToken cancellationToken);
}

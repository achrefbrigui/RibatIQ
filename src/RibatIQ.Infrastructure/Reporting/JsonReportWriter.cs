using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using RibatIQ.Application.Reporting;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Infrastructure.Reporting;

/// <summary>
/// Writes reports as JSON for tools and CI. The document is versioned with <see cref="SchemaVersion"/>;
/// fields are only added in a compatible way within one version. It never contains source code,
/// prompts or credentials.
/// </summary>
public sealed class JsonReportWriter : IReportWriter
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // Reports are files and pipes, not HTML: keep quotes and non-ASCII text readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Format => "json";

    public async Task WriteAsync(AnalysisReport report, TextWriter writer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var json = JsonSerializer.Serialize(ToDocument(report), Options);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    internal static ReportDocument ToDocument(AnalysisReport report)
    {
        var result = report.Result;
        var summary = result.Summary;

        return new ReportDocument(
            SchemaVersion,
            new ToolDocument("RibatIQ", result.Metadata.ToolVersion),
            result.Kind.ToString().ToLowerInvariant(),
            report.DisplayTarget,
            result.StartedAt,
            result.CompletedAt,
            (long)result.Duration.TotalMilliseconds,
            report.Coverage,
            new AiDocument(
                result.Metadata.AiEnabled,
                result.Metadata.Provider,
                result.Metadata.Model,
                report.Ai?.FilesSent,
                report.Ai?.Usage?.InputTokens,
                report.Ai?.Usage?.OutputTokens),
            new StatisticsDocument(
                report.Statistics.FilesAnalyzed,
                report.Statistics.ProjectCount,
                report.Statistics.ChangedFileCount,
                report.Statistics.FindingsOutsideChanges),
            new SummaryDocument(
                summary.Total, summary.Critical, summary.High, summary.Medium, summary.Low, summary.Info,
                summary.StaticAnalysis, summary.Ai),
            new GateDocument(report.FailOn.ToString(), report.FailingFindings, report.ThresholdExceeded),
            result.Findings.Select(ToDocument).ToArray(),
            result.Warnings.ToArray());
    }

    private static FindingDocument ToDocument(ReviewFinding finding) => new(
        finding.Id,
        finding.Title,
        finding.Description,
        finding.Severity,
        finding.Category,
        finding.Origin,
        finding.Source,
        finding.RuleId,
        finding.FilePath,
        finding.StartLine,
        finding.EndLine,
        finding.SuggestedFix,
        finding.Confidence);

    internal sealed record ReportDocument(
        int SchemaVersion,
        ToolDocument Tool,
        string Kind,
        string Target,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        long DurationMs,
        string Coverage,
        AiDocument Ai,
        StatisticsDocument Statistics,
        SummaryDocument Summary,
        GateDocument Gate,
        FindingDocument[] Findings,
        string[] Warnings);

    /// <summary>Outcome of the failure threshold: <c>failOn</c> is <c>none</c> or a severity.</summary>
    internal sealed record GateDocument(string FailOn, int FailingFindings, bool ThresholdExceeded);

    internal sealed record ToolDocument(string Name, string Version);

    internal sealed record AiDocument(bool Enabled, string? Provider, string? Model, int? FilesSent, long? InputTokens, long? OutputTokens);

    internal sealed record StatisticsDocument(int FilesAnalyzed, int? Projects, int? ChangedFiles, int? FindingsOutsideChanges);

    internal sealed record SummaryDocument(int Total, int Critical, int High, int Medium, int Low, int Info, int StaticAnalysis, int Ai);

    internal sealed record FindingDocument(
        string Id,
        string Title,
        string Description,
        FindingSeverity Severity,
        FindingCategory Category,
        FindingOrigin Origin,
        string Source,
        string? RuleId,
        string File,
        int? StartLine,
        int? EndLine,
        string? SuggestedFix,
        double? Confidence);
}

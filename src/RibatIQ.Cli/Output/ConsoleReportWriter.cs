using System.Globalization;

using RibatIQ.Application.Configuration;
using RibatIQ.Application.Instructions;
using RibatIQ.Application.Reporting;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;

namespace RibatIQ.Cli.Output;

/// <summary>Human-readable report: one line per finding, most severe first, then a summary.</summary>
internal sealed class ConsoleReportWriter : IReportWriter
{
    public const string Disclaimer = "RibatIQ reports likely issues; it does not prove that code is correct or secure.";

    public string Format => "console";

    public async Task WriteAsync(AnalysisReport report, TextWriter writer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var result = report.Result;

        await writer.WriteLineAsync($"RibatIQ {result.Kind.ToString().ToLowerInvariant()}: {report.DisplayTarget}").ConfigureAwait(false);
        await writer.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{ScopeLine(report)} Duration: {result.Duration.TotalSeconds:0.0}s. AI analysis: {(result.Metadata.AiEnabled ? "enabled" : "disabled")}.")).ConfigureAwait(false);

        if (report.Ai is { } ai)
        {
            var tokens = ai.Usage is { } usage
                ? string.Create(CultureInfo.InvariantCulture, $", {usage.InputTokens} input / {usage.OutputTokens} output tokens")
                : string.Empty;
            await writer.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"AI: {ai.ProviderId} ({ai.Model}), {ai.FilesSent} file(s) sent{tokens}. AI findings are heuristic; verify them before acting.")).ConfigureAwait(false);
        }

        if (report.Policy is { } policy)
        {
            await writer.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"Policy: profile {policy.Profile}, {policy.EnabledRules.Count} rule(s) enabled, minimum severity {SeverityNames.Of(policy.MinimumSeverity)}.")).ConfigureAwait(false);
        }

        if (InstructionsLine(report) is { } instructionsLine)
        {
            await writer.WriteLineAsync(instructionsLine).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);

        if (result.Findings.Count == 0)
        {
            await writer.WriteLineAsync("No findings.").ConfigureAwait(false);
        }

        foreach (var finding in result.Findings)
        {
            var confidence = finding.Confidence is { } c
                ? string.Create(CultureInfo.InvariantCulture, $" (confidence {c:0.00})")
                : string.Empty;

            await writer.WriteLineAsync(
                $"{finding.Severity.ToString().ToUpperInvariant(),-8} {FormatLocation(finding)}  [{RuleTag(finding)}] {finding.Title}: {finding.Description}{confidence}")
                .ConfigureAwait(false);

            if (finding.SuggestedFix is { } fix)
            {
                await writer.WriteLineAsync($"         Suggested fix: {fix}").ConfigureAwait(false);
            }
        }

        var s = result.Summary;
        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{s.Total} finding(s): {s.Critical} critical, {s.High} high, {s.Medium} medium, {s.Low} low, {s.Info} info.")).ConfigureAwait(false);

        if (result.Warnings.Count > 0)
        {
            await writer.WriteLineAsync("Warnings:").ConfigureAwait(false);
            foreach (var warning in result.Warnings)
            {
                await writer.WriteLineAsync($"  - {warning}").ConfigureAwait(false);
            }
        }

        if (result.Checks.Count > 0)
        {
            await writer.WriteLineAsync("Checks:").ConfigureAwait(false);
            foreach (var check in result.Checks)
            {
                await writer.WriteLineAsync(CheckLine(check, result.CountFindings(check))).ConfigureAwait(false);
            }

            if (!result.AllChecksCompleted)
            {
                var notRun = result.Checks.Where(c => c.Status != CheckStatus.Completed).Select(c => c.Name);
                await writer.WriteLineAsync(
                    $"Incomplete analysis: {string.Join(", ", notRun)} did not run or failed; no finding from them does not mean no issue.")
                    .ConfigureAwait(false);
            }
        }

        await writer.WriteLineAsync($"Coverage: {report.Coverage}.").ConfigureAwait(false);
        await writer.WriteLineAsync(Disclaimer).ConfigureAwait(false);

        if (report.FailOn.Threshold is not null)
        {
            await writer.WriteLineAsync(report.ThresholdExceeded
                ? string.Create(CultureInfo.InvariantCulture, $"Result: FAILED - {report.FailingFindings} finding(s) at or above '{report.FailOn}' (fail-on).")
                : $"Result: PASSED - no finding at or above '{report.FailOn}' (fail-on).").ConfigureAwait(false);
        }
    }

    /// <summary>One check: status, name, rule count and findings, or why it did not run.</summary>
    private static string CheckLine(CheckResult check, int findings)
    {
        var status = check.Status switch
        {
            CheckStatus.Completed => "completed",
            CheckStatus.Skipped => "skipped",
            _ => "FAILED",
        };

        var detail = check.Status == CheckStatus.Completed
            ? string.Create(CultureInfo.InvariantCulture, $" ({check.RuleIds.Count} rule(s), {findings} finding(s))")
            : string.Empty;
        var reason = check.Reason is { } r ? $": {r}" : string.Empty;

        return $"  {status,-9} {check.Name}{detail}{reason}";
    }

    /// <summary>
    /// Where project instructions came from, and whether they were used. Problems are already listed as
    /// warnings; a missing file is normal and not mentioned.
    /// </summary>
    private static string? InstructionsLine(AnalysisReport report)
    {
        if (report.Instructions is not { Instructions: { } instructions } loaded)
        {
            return null;
        }

        var truncated = loaded.Status == InstructionsStatus.Truncated ? ", truncated" : string.Empty;
        var aiRan = report.Result.Checks.Any(c => c.CheckId == AiCheckId && c.Status == CheckStatus.Completed);

        return aiRan
            ? $"Project instructions: {instructions.Source} (sent to the AI review as untrusted context{truncated})."
            : $"Project instructions: {instructions.Source} found but not used, because the AI review did not run.";
    }

    private const string AiCheckId = "ai-review";

    private static string ScopeLine(AnalysisReport report)
    {
        var stats = report.Statistics;
        return stats.ChangedFileCount is { } changed
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Reviewed {stats.FilesAnalyzed} changed C# file(s) of {changed} changed file(s); {stats.FindingsOutsideChanges ?? 0} finding(s) outside changed lines ignored.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Analyzed {stats.FilesAnalyzed} C# file(s) in {stats.ProjectCount ?? 0} project(s).");
    }

    /// <summary>Catalogue rule with the tool diagnostic in parentheses, e.g. <c>SECURITY.EXPOSED_SECRET (RIQ1004)</c>.</summary>
    private static string RuleTag(ReviewFinding finding) => (finding.RuleId, finding.ToolRuleId) switch
    {
        ({ } rule, { } tool) => $"{rule.Value} ({tool})",
        ({ } rule, null) => rule.Value,
        (null, { } tool) => tool,
        _ => finding.Source,
    };

    private static string FormatLocation(ReviewFinding finding) =>
        finding.StartLine is { } line
            ? string.Create(CultureInfo.InvariantCulture, $"{finding.FilePath}:{line}")
            : finding.FilePath;
}

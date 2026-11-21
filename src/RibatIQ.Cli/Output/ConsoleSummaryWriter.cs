using System.Globalization;

using RibatIQ.Application.Ai;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;

namespace RibatIQ.Cli.Output;

/// <summary>
/// Plain-text summary of an analysis result. Intentionally minimal; richer console and JSON
/// reports are planned with the reporting feature.
/// </summary>
internal static class ConsoleSummaryWriter
{
    public const string Disclaimer = "RibatIQ reports likely issues; it does not prove that code is correct or secure.";

    public static async Task WriteAsync(
        TextWriter writer, AnalysisResult result, string scopeLine, string coverage, AiAnalysisResult? ai = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        await writer.WriteLineAsync($"RibatIQ {result.Kind.ToString().ToLowerInvariant()}: {result.Target}").ConfigureAwait(false);
        await writer.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{scopeLine} Duration: {result.Duration.TotalSeconds:0.0}s. AI analysis: {(result.Metadata.AiEnabled ? "enabled" : "disabled")}.")).ConfigureAwait(false);

        if (ai is not null)
        {
            var tokens = ai.Usage is { } usage
                ? string.Create(CultureInfo.InvariantCulture, $", {usage.InputTokens} input / {usage.OutputTokens} output tokens")
                : string.Empty;
            await writer.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"AI: {ai.ProviderId} ({ai.Model}), {ai.FilesSent} file(s) sent{tokens}. AI findings are heuristic; verify them before acting.")).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);

        if (result.Findings.Count == 0)
        {
            await writer.WriteLineAsync("No findings.").ConfigureAwait(false);
        }

        foreach (var finding in result.Findings)
        {
            await writer.WriteLineAsync(
                $"{finding.Severity.ToString().ToUpperInvariant(),-8} {FormatLocation(finding)}  [{finding.RuleId ?? finding.Source}] {finding.Title}: {finding.Description}")
                .ConfigureAwait(false);
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

        await writer.WriteLineAsync($"Coverage: {coverage}.").ConfigureAwait(false);
        await writer.WriteLineAsync(Disclaimer).ConfigureAwait(false);
    }

    private static string FormatLocation(ReviewFinding finding) =>
        finding.StartLine is { } line
            ? string.Create(CultureInfo.InvariantCulture, $"{finding.FilePath}:{line}")
            : finding.FilePath;
}

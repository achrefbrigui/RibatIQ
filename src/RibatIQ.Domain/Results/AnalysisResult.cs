using System.Collections.ObjectModel;

using RibatIQ.Domain.Findings;

namespace RibatIQ.Domain.Results;

/// <summary>
/// Outcome of a completed scan or review. Findings are de-duplicated by the caller beforehand and
/// stored in a deterministic order (most severe first, then path, line and ID) so reports are stable.
/// </summary>
/// <remarks>
/// A result only represents a run that completed. Operational failures (Git, provider, I/O) must be
/// reported as failures, never as a result with zero findings.
/// </remarks>
public sealed class AnalysisResult
{
    public AnalysisResult(
        AnalysisKind kind,
        string target,
        IEnumerable<ReviewFinding> findings,
        AnalysisMetadata metadata,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IEnumerable<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(metadata);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined analysis kind.");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Target must not be empty.", nameof(target));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentException("Completion time must not precede start time.", nameof(completedAt));
        }

        var ordered = findings
            .Select(f => f ?? throw new ArgumentException("Findings must not contain null entries.", nameof(findings)))
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.FilePath, StringComparer.Ordinal)
            .ThenBy(f => f.StartLine ?? 0)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToArray();

        var duplicate = ordered.GroupBy(f => f.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Findings must have unique IDs; '{duplicate.Key}' appears more than once.", nameof(findings));
        }

        Kind = kind;
        Target = target.Trim();
        Findings = Array.AsReadOnly(ordered);
        Summary = FindingSummary.FromFindings(ordered);
        Metadata = metadata;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Warnings = Array.AsReadOnly(
            (warnings ?? [])
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim())
                .ToArray());
    }

    public AnalysisKind Kind { get; }

    /// <summary>What was analyzed: a solution/directory path for scans, a ref range for reviews.</summary>
    public string Target { get; }

    public ReadOnlyCollection<ReviewFinding> Findings { get; }

    public FindingSummary Summary { get; }

    public AnalysisMetadata Metadata { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>Non-fatal issues worth surfacing, such as skipped files or truncated context.</summary>
    public ReadOnlyCollection<string> Warnings { get; }
}

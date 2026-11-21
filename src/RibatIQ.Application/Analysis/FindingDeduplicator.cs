using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Analysis;

/// <summary>Collapses findings that share a fingerprint (same file, rule and line).</summary>
public static class FindingDeduplicator
{
    /// <summary>
    /// Keeps one finding per <see cref="ReviewFinding.Id"/>: deterministic analysis wins over AI,
    /// then the higher severity, then the first occurrence.
    /// </summary>
    public static IReadOnlyList<ReviewFinding> Deduplicate(IEnumerable<ReviewFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings
            .Select((finding, index) => (finding, index))
            .GroupBy(entry => entry.finding.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(entry => entry.finding.Origin == FindingOrigin.StaticAnalysis ? 0 : 1)
                .ThenByDescending(entry => entry.finding.Severity)
                .ThenBy(entry => entry.index)
                .First())
            .OrderBy(entry => entry.index)
            .Select(entry => entry.finding)
            .ToArray();
    }
}

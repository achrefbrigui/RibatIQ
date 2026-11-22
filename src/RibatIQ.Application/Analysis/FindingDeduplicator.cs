using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Analysis;

/// <summary>Removes findings that report the same issue twice.</summary>
/// <remarks>
/// Two findings are the same issue when they share a fingerprint (same file, rule and start line), or
/// when they belong to the same catalogue rule in the same file and their line ranges overlap. Findings
/// without a rule are only merged on identical fingerprints: there is no reliable evidence otherwise.
/// The kept finding is the deterministic one over the AI one, then the more severe, then the first.
/// </remarks>
public static class FindingDeduplicator
{
    public static IReadOnlyList<ReviewFinding> Deduplicate(IEnumerable<ReviewFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var indexed = findings.Select((finding, index) => (finding, index)).ToList();
        var kept = new List<(ReviewFinding Finding, int Index)>();

        foreach (var candidate in indexed.OrderBy(e => Rank(e.finding)).ThenBy(e => e.index))
        {
            if (!kept.Any(k => IsSameIssue(k.Finding, candidate.finding)))
            {
                kept.Add(candidate);
            }
        }

        return kept.OrderBy(k => k.Index).Select(k => k.Finding).ToArray();
    }

    private static (int Origin, int Severity) Rank(ReviewFinding finding) =>
        (finding.Origin == FindingOrigin.StaticAnalysis ? 0 : 1, -(int)finding.Severity);

    private static bool IsSameIssue(ReviewFinding a, ReviewFinding b)
    {
        if (string.Equals(a.Id, b.Id, StringComparison.Ordinal))
        {
            return true;
        }

        return a.RuleId is { } rule
            && rule.Equals(b.RuleId)
            && string.Equals(a.FilePath, b.FilePath, StringComparison.Ordinal)
            && a.StartLine is { } aStart && b.StartLine is { } bStart
            && aStart <= (b.EndLine ?? bStart)
            && bStart <= (a.EndLine ?? aStart);
    }
}

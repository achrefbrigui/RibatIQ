using RibatIQ.Domain.Findings;

namespace RibatIQ.Domain.Results;

/// <summary>Finding counts per severity and origin.</summary>
public sealed record FindingSummary(
    int Total,
    int Critical,
    int High,
    int Medium,
    int Low,
    int Info,
    int StaticAnalysis,
    int Ai)
{
    public static FindingSummary FromFindings(IEnumerable<ReviewFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var list = findings as IReadOnlyCollection<ReviewFinding> ?? findings.ToList();

        return new FindingSummary(
            Total: list.Count,
            Critical: list.Count(f => f.Severity == FindingSeverity.Critical),
            High: list.Count(f => f.Severity == FindingSeverity.High),
            Medium: list.Count(f => f.Severity == FindingSeverity.Medium),
            Low: list.Count(f => f.Severity == FindingSeverity.Low),
            Info: list.Count(f => f.Severity == FindingSeverity.Info),
            StaticAnalysis: list.Count(f => f.Origin == FindingOrigin.StaticAnalysis),
            Ai: list.Count(f => f.Origin == FindingOrigin.Ai));
    }

    /// <summary>Number of findings at or above <paramref name="threshold"/>.</summary>
    public int CountAtOrAbove(FindingSeverity threshold) => threshold switch
    {
        FindingSeverity.Critical => Critical,
        FindingSeverity.High => Critical + High,
        FindingSeverity.Medium => Critical + High + Medium,
        FindingSeverity.Low => Critical + High + Medium + Low,
        FindingSeverity.Info => Total,
        _ => throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Undefined severity."),
    };
}

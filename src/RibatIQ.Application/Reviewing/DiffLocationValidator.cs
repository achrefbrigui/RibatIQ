using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Reviewing;

/// <summary>
/// Keeps only findings attributable to a change: located in a changed file and overlapping its
/// added lines. Used for deterministic findings now and for AI findings later, whose locations are
/// untrusted.
/// </summary>
public static class DiffLocationValidator
{
    /// <returns>The findings that touch changed lines, and how many were dropped.</returns>
    public static (IReadOnlyList<ReviewFinding> Kept, int Dropped) KeepChangedLines(
        IEnumerable<ReviewFinding> findings, IEnumerable<FileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(changes);

        var byPath = changes
            .Where(c => c.Kind != ChangeKind.Deleted)
            .ToDictionary(c => c.Path, StringComparer.Ordinal);

        var kept = new List<ReviewFinding>();
        var dropped = 0;

        foreach (var finding in findings)
        {
            if (byPath.TryGetValue(finding.FilePath, out var change) && IsAttributable(finding, change))
            {
                kept.Add(finding);
            }
            else
            {
                dropped++;
            }
        }

        return (kept, dropped);
    }

    private static bool IsAttributable(ReviewFinding finding, FileChange change) =>
        finding.StartLine is { } start
            ? change.TouchesAddedLines(start, finding.EndLine ?? start)
            : change.Kind == ChangeKind.Added; // File-level findings only belong to new files.
}

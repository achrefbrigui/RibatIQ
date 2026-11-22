using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Reviewing;

/// <summary>How a file changed between two revisions.</summary>
public enum ChangeKind
{
    Modified = 0,
    Added,
    Deleted,
    Renamed,
}

/// <summary>An inclusive, 1-based range of lines.</summary>
public readonly record struct LineRange
{
    public LineRange(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        Start = start;
        End = end;
    }

    public int Start { get; }

    public int End { get; }

    public bool Overlaps(int start, int end) => start <= End && end >= Start;
}

/// <summary>A file changed in a diff, with the lines added or modified in the new revision.</summary>
public sealed record FileChange
{
    public FileChange(string path, string? oldPath, ChangeKind kind, IReadOnlyList<LineRange> addedLines)
    {
        ArgumentNullException.ThrowIfNull(addedLines);

        Path = RepositoryPath.Normalize(path, nameof(path));
        OldPath = oldPath is null ? null : RepositoryPath.Normalize(oldPath, nameof(oldPath));
        Kind = kind;
        AddedLines = addedLines;
    }

    /// <summary>Repository-relative path in the new revision (the old path for deletions).</summary>
    public string Path { get; }

    /// <summary>Previous path of a renamed file.</summary>
    public string? OldPath { get; }

    public ChangeKind Kind { get; }

    /// <summary>Lines added or modified in the new revision, ordered and non-overlapping.</summary>
    public IReadOnlyList<LineRange> AddedLines { get; }

    /// <summary>Whether lines <paramref name="start"/>..<paramref name="end"/> touch an added line.</summary>
    public bool TouchesAddedLines(int start, int end) => AddedLines.Any(range => range.Overlaps(start, end));
}

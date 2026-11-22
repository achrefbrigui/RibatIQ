using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RibatIQ.Application.Reviewing;

/// <summary>
/// Parses <c>git diff</c> unified output into <see cref="FileChange"/>s with the added line numbers of
/// the new revision. Hunk bodies are consumed by line count, so content lines that look like headers
/// (for example an added line starting with <c>++</c>) are never misread.
/// </summary>
public static partial class UnifiedDiffParser
{
    public static IReadOnlyList<FileChange> Parse(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var changes = new List<FileChange>();
        FileState? current = null;
        int oldRemaining = 0, newRemaining = 0, newLine = 0;

        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (oldRemaining > 0 || newRemaining > 0)
            {
                if (line.StartsWith('\\'))
                {
                    continue; // "\ No newline at end of file"
                }

                var marker = line.Length > 0 ? line[0] : ' ';
                if (marker == '+')
                {
                    current?.AddLine(newLine);
                    newLine++;
                    newRemaining--;
                }
                else if (marker == '-')
                {
                    oldRemaining--;
                }
                else
                {
                    newLine++;
                    newRemaining--;
                    oldRemaining--;
                }

                continue;
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Complete(current, changes);
                current = new FileState();
            }
            else if (current is null)
            {
                continue;
            }
            else if (line.StartsWith("new file mode", StringComparison.Ordinal))
            {
                current.Kind = ChangeKind.Added;
            }
            else if (line.StartsWith("deleted file mode", StringComparison.Ordinal))
            {
                current.Kind = ChangeKind.Deleted;
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                current.Kind = ChangeKind.Renamed;
                current.OldPath = Unquote(line["rename from ".Length..]);
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                current.Kind = ChangeKind.Renamed;
                current.NewPath = Unquote(line["rename to ".Length..]);
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                current.OldPath ??= StripPrefix(Unquote(line[4..]), "a/");
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                current.NewPath = StripPrefix(Unquote(line[4..]), "b/");
            }
            else if (HunkHeader().Match(line) is { Success: true } hunk)
            {
                oldRemaining = ParseCount(hunk.Groups["oldCount"]);
                newRemaining = ParseCount(hunk.Groups["newCount"]);
                newLine = int.Parse(hunk.Groups["newStart"].Value, CultureInfo.InvariantCulture);
            }
        }

        Complete(current, changes);
        return changes;
    }

    private static void Complete(FileState? state, List<FileChange> changes)
    {
        if (state is null)
        {
            return;
        }

        var newPath = state.NewPath == DevNull ? null : state.NewPath;
        var oldPath = state.OldPath == DevNull ? null : state.OldPath;

        if (newPath is null && oldPath is null)
        {
            return; // Binary or mode-only change without paths in the patch.
        }

        var kind = newPath is null ? ChangeKind.Deleted : oldPath is null ? ChangeKind.Added : state.Kind;
        var path = newPath ?? oldPath!;
        var previous = kind == ChangeKind.Renamed ? oldPath : null;

        changes.Add(new FileChange(path, previous, kind, state.BuildRanges()));
    }

    private const string DevNull = "/dev/null";

    private static int ParseCount(Group group) =>
        group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 1;

    private static string StripPrefix(string path, string prefix) =>
        path != DevNull && path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;

    /// <summary>Decodes Git's C-style quoting (used for paths with special characters).</summary>
    internal static string Unquote(string value)
    {
        var text = value.TrimEnd('\t');
        if (text.Length < 2 || text[0] != '"' || text[^1] != '"')
        {
            return text;
        }

        var bytes = new List<byte>();
        for (var i = 1; i < text.Length - 1; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length - 1)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
                continue;
            }

            var next = text[++i];
            if (next is >= '0' and <= '7' && i + 2 < text.Length - 1)
            {
                bytes.Add(Convert.ToByte(text.Substring(i, 3), 8));
                i += 2;
                continue;
            }

            bytes.Add(next switch
            {
                'n' => (byte)'\n',
                't' => (byte)'\t',
                'r' => (byte)'\r',
                'a' => 7,
                'b' => 8,
                'f' => 12,
                'v' => 11,
                _ => (byte)next,
            });
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    [GeneratedRegex(@"^@@ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HunkHeader();

    private sealed class FileState
    {
        private readonly List<int> _addedLines = [];

        public ChangeKind Kind { get; set; } = ChangeKind.Modified;

        public string? OldPath { get; set; }

        public string? NewPath { get; set; }

        public void AddLine(int line) => _addedLines.Add(line);

        public LineRange[] BuildRanges()
        {
            var ranges = new List<LineRange>();
            foreach (var line in _addedLines.Order())
            {
                if (ranges.Count > 0 && line <= ranges[^1].End + 1)
                {
                    ranges[^1] = new LineRange(ranges[^1].Start, Math.Max(ranges[^1].End, line));
                }
                else
                {
                    ranges.Add(new LineRange(line, line));
                }
            }

            return [.. ranges];
        }
    }
}

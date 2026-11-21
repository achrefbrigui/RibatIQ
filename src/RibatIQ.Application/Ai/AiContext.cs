using System.Globalization;
using System.Text;

using RibatIQ.Application.Reviewing;
using RibatIQ.Application.Sources;

namespace RibatIQ.Application.Ai;

/// <summary>A source excerpt sent to the model: which lines it shows and how they were rendered.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="LineCount">Number of lines in the whole file.</param>
/// <param name="Windows">Line ranges included in the excerpt.</param>
/// <param name="ChangedLines">Lines added or modified by the change under review (empty for scans).</param>
/// <param name="Rendered">Line-numbered text, changed lines marked with '+'.</param>
public sealed record AiFileExcerpt(
    string Path, int LineCount, IReadOnlyList<LineRange> Windows, IReadOnlyList<LineRange> ChangedLines, string Rendered)
{
    /// <summary>Whether lines <paramref name="start"/>..<paramref name="end"/> were all visible to the model.</summary>
    public bool Shows(int start, int end) => Windows.Any(w => start >= w.Start && end <= w.End);
}

/// <summary>Everything the model is shown for one request, within the configured budget.</summary>
public sealed record AiContext(IReadOnlyList<AiFileExcerpt> Files, IReadOnlyList<string> Warnings)
{
    public int TotalChars => Files.Sum(f => f.Rendered.Length);

    public AiFileExcerpt? Find(string path) => Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal));
}

/// <summary>Selects bounded, line-numbered source context for AI analysis.</summary>
public static class AiContextBuilder
{
    /// <summary>Whole files, in order, until the budget is used. Files that do not fit are skipped with a warning.</summary>
    public static AiContext ForScan(IReadOnlyList<SourceDocument> documents, AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(options);

        var excerpts = new List<AiFileExcerpt>();
        foreach (var document in documents)
        {
            var lines = SplitLines(document.Text);
            if (lines.Length == 0)
            {
                continue;
            }

            excerpts.Add(Render(document.Path, lines, [new LineRange(1, lines.Length)], changed: []));
        }

        return ApplyBudget(excerpts, options, "scan");
    }

    /// <summary>
    /// For each changed file, the changed lines plus <see cref="AiOptions.ReviewContextLines"/> lines around them.
    /// Excerpts that do not fit the budget are skipped with a warning.
    /// </summary>
    public static AiContext ForReview(IReadOnlyList<SourceDocument> documents, IReadOnlyList<FileChange> changes, AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(options);

        var changesByPath = changes.ToDictionary(c => c.Path, StringComparer.Ordinal);
        var excerpts = new List<AiFileExcerpt>();

        foreach (var document in documents)
        {
            if (!changesByPath.TryGetValue(document.Path, out var change) || change.AddedLines.Count == 0)
            {
                continue;
            }

            var lines = SplitLines(document.Text);
            if (lines.Length == 0)
            {
                continue;
            }

            var changed = change.AddedLines
                .Where(r => r.Start <= lines.Length)
                .Select(r => new LineRange(r.Start, Math.Min(r.End, lines.Length)))
                .ToArray();

            var windows = Merge(changed.Select(r => new LineRange(
                Math.Max(1, r.Start - options.ReviewContextLines),
                Math.Min(lines.Length, r.End + options.ReviewContextLines))));

            if (windows.Count > 0)
            {
                excerpts.Add(Render(document.Path, lines, [.. windows], changed));
            }
        }

        return ApplyBudget(excerpts, options, "review");
    }

    internal static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    private static AiContext ApplyBudget(List<AiFileExcerpt> excerpts, AiOptions options, string kind)
    {
        var accepted = new List<AiFileExcerpt>();
        var warnings = new List<string>();
        var total = 0;
        var tooLarge = 0;
        var overBudget = 0;

        foreach (var excerpt in excerpts)
        {
            if (excerpt.Rendered.Length > options.MaxFileChars)
            {
                tooLarge++;
                continue;
            }

            if (total + excerpt.Rendered.Length > options.MaxPromptChars)
            {
                overBudget++;
                continue;
            }

            accepted.Add(excerpt);
            total += excerpt.Rendered.Length;
        }

        if (tooLarge > 0)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"AI {kind}: {tooLarge} file(s) larger than {options.MaxFileChars} characters were not sent to the model."));
        }

        if (overBudget > 0)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"AI {kind}: {overBudget} file(s) were not sent to the model because the context budget ({options.MaxPromptChars} characters) was reached. Narrow the run with --include or --exclude."));
        }

        return new AiContext(accepted, warnings);
    }

    private static List<LineRange> Merge(IEnumerable<LineRange> ranges)
    {
        var merged = new List<LineRange>();
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + 1)
            {
                merged[^1] = new LineRange(merged[^1].Start, Math.Max(merged[^1].End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    private static AiFileExcerpt Render(string path, string[] lines, LineRange[] windows, LineRange[] changed)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < windows.Length; i++)
        {
            if (i > 0)
            {
                builder.Append("     ...\n");
            }

            for (var line = windows[i].Start; line <= windows[i].End; line++)
            {
                var marker = changed.Any(c => line >= c.Start && line <= c.End) ? '+' : ' ';
                builder.Append(CultureInfo.InvariantCulture, $"{marker}{line,6} | {lines[line - 1]}\n");
            }
        }

        return new AiFileExcerpt(path, lines.Length, windows, changed, builder.ToString());
    }
}

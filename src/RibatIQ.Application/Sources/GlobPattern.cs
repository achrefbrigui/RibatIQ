using System.Text;
using System.Text.RegularExpressions;

namespace RibatIQ.Application.Sources;

/// <summary>
/// A file glob matched against repository-relative paths (forward slashes), case-insensitively.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>*</c> matches within one path segment, <c>?</c> one character in a segment.</item>
/// <item><c>**</c> matches any number of segments, including none (<c>**/bin/**</c> matches <c>bin/a.cs</c>).</item>
/// <item>A pattern without <c>/</c> matches at any depth: <c>*.g.cs</c> is treated as <c>**/*.g.cs</c>.</item>
/// </list>
/// </remarks>
public sealed class GlobPattern
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex _regex;

    public GlobPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Glob pattern must not be empty.", nameof(pattern));
        }

        Pattern = pattern.Trim();
        _regex = new Regex(
            ToRegex(Pattern),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            MatchTimeout);
    }

    public string Pattern { get; }

    public bool IsMatch(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return _regex.IsMatch(relativePath.Replace('\\', '/'));
    }

    public override string ToString() => Pattern;

    private static string ToRegex(string pattern)
    {
        var glob = pattern.Replace('\\', '/');
        while (glob.StartsWith("./", StringComparison.Ordinal))
        {
            glob = glob[2..];
        }

        glob = glob.TrimStart('/');
        if (!glob.Contains('/', StringComparison.Ordinal))
        {
            glob = "**/" + glob;
        }

        var regex = new StringBuilder("^");
        var i = 0;
        while (i < glob.Length)
        {
            if (string.CompareOrdinal(glob, i, "**/", 0, 3) == 0)
            {
                regex.Append("(?:.*/)?");
                i += 3;
            }
            else if (i == glob.Length - 3 && string.CompareOrdinal(glob, i, "/**", 0, 3) == 0)
            {
                regex.Append("(?:/.*)?");
                i += 3;
            }
            else if (string.CompareOrdinal(glob, i, "**", 0, 2) == 0)
            {
                regex.Append(".*");
                i += 2;
            }
            else
            {
                regex.Append(glob[i] switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    var c => Regex.Escape(c.ToString()),
                });
                i++;
            }
        }

        return regex.Append('$').ToString();
    }
}

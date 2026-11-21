using RibatIQ.Application.Configuration;

namespace RibatIQ.Application.Sources;

/// <summary>Decides which repository-relative paths are analyzed, from include and exclude globs.</summary>
public sealed class PathFilter
{
    private readonly GlobPattern[] _include;
    private readonly GlobPattern[] _exclude;

    public PathFilter(IEnumerable<string> include, IEnumerable<string> exclude)
    {
        ArgumentNullException.ThrowIfNull(include);
        ArgumentNullException.ThrowIfNull(exclude);

        _include = include.Select(p => new GlobPattern(p)).ToArray();
        _exclude = exclude.Select(p => new GlobPattern(p)).ToArray();
    }

    public static PathFilter FromSettings(RibatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new PathFilter(settings.Include, settings.Exclude);
    }

    /// <summary>
    /// A file is included when it matches an include pattern (or there are none)
    /// and matches no exclude pattern.
    /// </summary>
    public bool IsIncluded(string relativePath) =>
        (_include.Length == 0 || _include.Any(p => p.IsMatch(relativePath)))
        && !_exclude.Any(p => p.IsMatch(relativePath));

    /// <summary>
    /// Whether everything under <paramref name="relativeDirectory"/> is excluded, so discovery can skip it.
    /// </summary>
    public bool IsDirectoryExcluded(string relativeDirectory)
    {
        ArgumentNullException.ThrowIfNull(relativeDirectory);
        var directory = relativeDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        return _exclude.Any(p => p.IsMatch(directory));
    }
}

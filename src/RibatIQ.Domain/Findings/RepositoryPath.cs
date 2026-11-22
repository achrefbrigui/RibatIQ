namespace RibatIQ.Domain.Findings;

/// <summary>
/// Normalizes and validates file paths stored in findings. Paths are repository-relative and use
/// forward slashes, so reports are portable across machines and never leak absolute local paths.
/// </summary>
public static class RepositoryPath
{
    /// <summary>
    /// Returns the normalized form of <paramref name="path"/>: trimmed, forward slashes,
    /// no leading <c>./</c>, no duplicate separators.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is empty, absolute, contains <c>..</c> segments, or contains control characters.
    /// </exception>
    public static string Normalize(string path, string? paramName = null)
    {
        paramName ??= nameof(path);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("File path must not be empty.", paramName);
        }

        var normalized = path.Trim().Replace('\\', '/');

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("File path must not contain control characters.", paramName);
        }

        if (IsAbsolute(normalized))
        {
            throw new ArgumentException($"File path must be repository-relative, but was '{normalized}'.", paramName);
        }

        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();

        if (segments.Length == 0)
        {
            throw new ArgumentException("File path must name a file.", paramName);
        }

        if (segments.Contains(".."))
        {
            throw new ArgumentException($"File path must not contain '..' segments, but was '{normalized}'.", paramName);
        }

        return string.Join('/', segments);
    }

    private static bool IsAbsolute(string path) =>
        path.StartsWith('/')
        || path.StartsWith('~')
        || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
}

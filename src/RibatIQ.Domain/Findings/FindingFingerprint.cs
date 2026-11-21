using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RibatIQ.Domain.Findings;

/// <summary>
/// Computes stable finding identifiers suitable for de-duplication and future baseline comparison.
/// </summary>
/// <remarks>
/// The fingerprint depends only on the normalized file path, the rule key (rule ID, or the normalized
/// title when there is no rule ID) and the start line. It deliberately ignores severity, description,
/// confidence and the producing provider, so the same issue keeps the same ID when those vary.
/// Moving code to a different line changes the fingerprint. The algorithm is versioned; changing it
/// requires bumping <see cref="AlgorithmVersion"/>.
/// </remarks>
public static class FindingFingerprint
{
    /// <summary>Version tag mixed into the hash so that algorithm changes never collide with old IDs.</summary>
    public const string AlgorithmVersion = "v1";

    /// <summary>Length in characters of a fingerprint (lowercase hexadecimal).</summary>
    public const int Length = 32;

    public static string Compute(string filePath, string? ruleId, string title, int? startLine)
    {
        var path = RepositoryPath.Normalize(filePath, nameof(filePath));
        var ruleKey = string.IsNullOrWhiteSpace(ruleId)
            ? "title:" + NormalizeTitle(title)
            : "rule:" + ruleId.Trim();
        var line = startLine?.ToString(CultureInfo.InvariantCulture) ?? "-";

        var payload = string.Join('\n', AlgorithmVersion, path, ruleKey, line);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexStringLower(hash.AsSpan(0, Length / 2));
    }

    /// <summary>Returns whether <paramref name="value"/> has the shape of a fingerprint.</summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: Length } && value.All(char.IsAsciiHexDigitLower);

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title must not be empty when no rule ID is provided.", nameof(title));
        }

        var words = title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).ToLowerInvariant();
    }
}

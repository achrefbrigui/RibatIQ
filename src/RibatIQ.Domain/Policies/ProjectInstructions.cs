using System.Text;

namespace RibatIQ.Domain.Policies;

/// <summary>
/// Project-specific review guidance (for example from <c>review-rules.md</c>). Always untrusted data:
/// it can steer what reviewers look at, but never changes policies, output formats or permissions.
/// </summary>
public sealed record ProjectInstructions
{
    /// <summary>Largest amount of instruction text kept, in characters.</summary>
    public const int MaxLength = 16_000;

    private ProjectInstructions(string text, string source, bool truncated)
    {
        Text = text;
        Source = source;
        Truncated = truncated;
    }

    /// <summary>Normalized instruction text (LF line endings, no control characters other than tab and newline).</summary>
    public string Text { get; }

    /// <summary>Where the text came from, for example <c>review-rules.md</c>.</summary>
    public string Source { get; }

    /// <summary>Whether the original text exceeded <see cref="MaxLength"/> and was cut.</summary>
    public bool Truncated { get; }

    /// <summary>
    /// Normalizes and bounds <paramref name="text"/>. Returns <see langword="null"/> when nothing usable remains.
    /// </summary>
    public static ProjectInstructions? Create(string? text, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(text.Length, MaxLength + 1));
        foreach (var c in text.ReplaceLineEndings("\n"))
        {
            if (c is '\n' or '\t' || !char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        var normalized = builder.ToString().Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        var truncated = normalized.Length > MaxLength;
        if (truncated)
        {
            normalized = normalized[..MaxLength];
            if (char.IsHighSurrogate(normalized[^1]))
            {
                normalized = normalized[..^1];
            }
        }

        return new ProjectInstructions(normalized, source.Trim(), truncated);
    }
}

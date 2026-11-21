namespace RibatIQ.Domain.Rules;

/// <summary>
/// Stable identifier of a RibatIQ review rule, for example <c>SECURITY.EXPOSED_SECRET</c>.
/// </summary>
/// <remarks>
/// Format: two to six dot-separated segments of upper-case ASCII letters, digits and underscores, each
/// starting with a letter, at most <see cref="MaxLength"/> characters. Identifiers are compared ordinally (sort with <c>StringComparer.Ordinal</c> on <see cref="Value"/>).
/// A well-formed identifier is not necessarily a supported rule: the rule catalogue decides that.
/// </remarks>
public sealed record ReviewRuleId
{
    public const int MaxLength = 100;
    private const int MinSegments = 2;
    private const int MaxSegments = 6;

    private ReviewRuleId(string value) => Value = value;

    public string Value { get; }

    /// <exception cref="ArgumentException"><paramref name="value"/> is not a well-formed rule identifier.</exception>
    public static ReviewRuleId Parse(string value) =>
        TryParse(value, out var id)
            ? id
            : throw new ArgumentException($"'{value}' is not a valid rule identifier (expected for example SECURITY.EXPOSED_SECRET).", nameof(value));

    /// <summary>Parses <paramref name="value"/> after trimming; letters must already be upper case.</summary>
    public static bool TryParse(string? value, out ReviewRuleId id)
    {
        id = null!;
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return false;
        }

        var segments = trimmed.Split('.');
        if (segments.Length is < MinSegments or > MaxSegments || !segments.All(IsValidSegment))
        {
            return false;
        }

        id = new ReviewRuleId(trimmed);
        return true;
    }

    public override string ToString() => Value;

    private static bool IsValidSegment(string segment) =>
        segment.Length > 0
        && char.IsAsciiLetterUpper(segment[0])
        && segment.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');
}

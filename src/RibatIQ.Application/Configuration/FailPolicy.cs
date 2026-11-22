using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Configuration;

/// <summary>
/// Severity at or above which findings make the run fail (exit code 1), or <see cref="Never"/>.
/// </summary>
public sealed record FailPolicy
{
    public const string NeverValue = "none";

    private FailPolicy(FindingSeverity? threshold) => Threshold = threshold;

    /// <summary>Findings never fail the run.</summary>
    public static FailPolicy Never { get; } = new(threshold: null);

    /// <summary>Accepted textual values, in increasing severity.</summary>
    public static IReadOnlyList<string> AcceptedValues { get; } = [NeverValue, .. SeverityNames.All];

    /// <summary>Lowest failing severity, or <see langword="null"/> for <see cref="Never"/>.</summary>
    public FindingSeverity? Threshold { get; }

    /// <summary>Number of findings at or above the threshold; always 0 for <see cref="Never"/>.</summary>
    public int CountFailing(FindingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return Threshold is { } threshold ? summary.CountAtOrAbove(threshold) : 0;
    }

    public static FailPolicy AtOrAbove(FindingSeverity severity) =>
        Enum.IsDefined(severity)
            ? new FailPolicy(severity)
            : throw new ArgumentOutOfRangeException(nameof(severity), severity, "Undefined severity.");

    /// <summary>Parses one of <see cref="AcceptedValues"/>, ignoring case and surrounding whitespace.</summary>
    public static bool TryParse(string? value, out FailPolicy policy)
    {
        if (string.Equals(value?.Trim(), NeverValue, StringComparison.OrdinalIgnoreCase))
        {
            policy = Never;
            return true;
        }

        if (SeverityNames.TryParse(value, out var severity))
        {
            policy = AtOrAbove(severity);
            return true;
        }

        policy = Never;
        return false;
    }

    public override string ToString() => Threshold is { } threshold ? SeverityNames.Of(threshold) : NeverValue;
}

using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Configuration;

/// <summary>
/// Severity at or above which findings make the run fail (exit code 1), or <see cref="Never"/>.
/// </summary>
public sealed record FailPolicy
{
    public const string NeverValue = "none";

    private static readonly (string Name, FindingSeverity Severity)[] SeverityNames =
    [
        ("info", FindingSeverity.Info),
        ("low", FindingSeverity.Low),
        ("medium", FindingSeverity.Medium),
        ("high", FindingSeverity.High),
        ("critical", FindingSeverity.Critical),
    ];

    private FailPolicy(FindingSeverity? threshold) => Threshold = threshold;

    /// <summary>Findings never fail the run.</summary>
    public static FailPolicy Never { get; } = new(threshold: null);

    /// <summary>Accepted textual values, in increasing severity.</summary>
    public static IReadOnlyList<string> AcceptedValues { get; } =
        [NeverValue, .. SeverityNames.Select(entry => entry.Name)];

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
        var trimmed = value?.Trim();

        if (string.Equals(trimmed, NeverValue, StringComparison.OrdinalIgnoreCase))
        {
            policy = Never;
            return true;
        }

        foreach (var (name, severity) in SeverityNames)
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase))
            {
                policy = AtOrAbove(severity);
                return true;
            }
        }

        policy = Never;
        return false;
    }

    public override string ToString() =>
        Threshold is { } threshold
            ? SeverityNames.First(entry => entry.Severity == threshold).Name
            : NeverValue;
}

using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Configuration;

/// <summary>Textual severity names used on the command line and in configuration files.</summary>
public static class SeverityNames
{
    private static readonly (string Name, FindingSeverity Severity)[] Entries =
    [
        ("info", FindingSeverity.Info),
        ("low", FindingSeverity.Low),
        ("medium", FindingSeverity.Medium),
        ("high", FindingSeverity.High),
        ("critical", FindingSeverity.Critical),
    ];

    /// <summary>Accepted names, in increasing severity.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Entries.Select(e => e.Name)];

    /// <summary>Parses a severity name, ignoring case and surrounding whitespace.</summary>
    public static bool TryParse(string? value, out FindingSeverity severity)
    {
        var trimmed = value?.Trim();
        foreach (var (name, entrySeverity) in Entries)
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase))
            {
                severity = entrySeverity;
                return true;
            }
        }

        severity = FindingSeverity.Info;
        return false;
    }

    /// <summary>The name of <paramref name="severity"/>.</summary>
    public static string Of(FindingSeverity severity) =>
        Entries.First(e => e.Severity == severity).Name;
}

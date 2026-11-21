namespace RibatIQ.Domain.Findings;

/// <summary>
/// Severity of a finding, ordered from least to most severe. The numeric order is relied upon
/// for threshold comparisons, so new values must preserve it.
/// </summary>
public enum FindingSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

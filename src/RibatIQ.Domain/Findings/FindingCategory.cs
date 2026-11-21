namespace RibatIQ.Domain.Findings;

/// <summary>Broad classification of what a finding is about.</summary>
public enum FindingCategory
{
    Unknown = 0,
    Correctness,
    Security,
    Performance,
    Maintainability,
    Reliability,
    Architecture,
    Style,
}

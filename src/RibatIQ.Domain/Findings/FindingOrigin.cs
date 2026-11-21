namespace RibatIQ.Domain.Findings;

/// <summary>
/// How a finding was produced. Deterministic analyzer output and LLM output carry different
/// levels of trust and must never be confused.
/// </summary>
public enum FindingOrigin
{
    /// <summary>Produced by a deterministic tool such as the C# compiler or a Roslyn analyzer.</summary>
    StaticAnalysis = 0,

    /// <summary>Produced by a large language model. Always heuristic; never authoritative.</summary>
    Ai = 1,
}

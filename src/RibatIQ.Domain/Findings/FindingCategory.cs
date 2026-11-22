namespace RibatIQ.Domain.Findings;

/// <summary>
/// The review area a finding belongs to. Stable values: they appear in reports and in AI responses.
/// </summary>
public enum FindingCategory
{
    /// <summary>Bugs: null references, logic errors, boundaries, exception handling, resource leaks, concurrency.</summary>
    Correctness = 0,

    /// <summary>N+1 queries, repeated external calls, needless allocations, inefficient LINQ, unbounded retrieval.</summary>
    Performance,

    /// <summary>Injection, exposed secrets, unsafe input, access control, path traversal, unsafe crypto or deserialization.</summary>
    Security,

    /// <summary>Known-vulnerable or outdated NuGet dependencies (software composition analysis).</summary>
    Dependencies,

    /// <summary>.NET and C# practices: async/await, cancellation, disposal, nullable reference types.</summary>
    BestPractices,

    /// <summary>Duplication, complexity, long methods, deep nesting, dead code, naming that hurts maintainability.</summary>
    CleanCode,

    /// <summary>Coupling, dependency direction, responsibilities, separation of concerns, documented architecture.</summary>
    Architecture,

    /// <summary>Missing or fragile tests for behavior that changed.</summary>
    Testing,

    /// <summary>Health checks, logging, timeouts and resilience, environment-specific configuration.</summary>
    ProductionReadiness,
}

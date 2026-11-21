using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Rules;

/// <summary>How a rule is detected. Each rule has exactly one mechanism.</summary>
public enum DetectionMethod
{
    /// <summary>Deterministic source analysis (Roslyn parser and RibatIQ analyzers).</summary>
    StaticAnalysis = 0,

    /// <summary>Dependency auditing with .NET SDK tooling.</summary>
    DependencyAudit,

    /// <summary>Contextual review by the configured LLM provider; findings are heuristic.</summary>
    AiReview,

    /// <summary>Analysis of test code and results.</summary>
    TestAnalysis,

    /// <summary>Analysis of existing coverage data.</summary>
    CoverageAnalysis,

    /// <summary>Analysis of configuration files.</summary>
    ConfigurationAnalysis,
}

/// <summary>A rule RibatIQ supports, as described by the rule catalogue.</summary>
/// <param name="Id">Stable rule identifier.</param>
/// <param name="Title">Short title, used for findings and reports.</param>
/// <param name="Category">Review area.</param>
/// <param name="DefaultSeverity">Severity of the rule's findings unless a detector has better evidence.</param>
/// <param name="EnabledByDefault">Whether the <c>default</c> profile enables the rule.</param>
/// <param name="Detection">How the rule is detected.</param>
/// <param name="Description">What the rule looks for; also given to AI reviewers.</param>
public sealed record RuleDefinition(
    ReviewRuleId Id,
    string Title,
    FindingCategory Category,
    FindingSeverity DefaultSeverity,
    bool EnabledByDefault,
    DetectionMethod Detection,
    string Description);

/// <summary>
/// A named starting point for a review policy. A profile never redefines rules: it starts from the rules
/// enabled by default, adds and removes rules, and can restrict the policy to some categories.
/// </summary>
/// <param name="Name">Profile name, for example <c>security</c>.</param>
/// <param name="Description">What the profile is for.</param>
/// <param name="Categories">Categories the profile is restricted to, or <see langword="null"/> for all.</param>
/// <param name="Enable">Rules enabled in addition to those enabled by default.</param>
/// <param name="Disable">Rules disabled from those enabled by default.</param>
public sealed record RuleProfile(
    string Name,
    string Description,
    IReadOnlySet<FindingCategory>? Categories,
    IReadOnlyList<ReviewRuleId> Enable,
    IReadOnlyList<ReviewRuleId> Disable);

/// <summary>The rule catalogue is invalid. This is a defect in RibatIQ, not a user error.</summary>
public sealed class RuleCatalogException : Exception
{
    public RuleCatalogException()
    {
    }

    public RuleCatalogException(string message)
        : base(message)
    {
    }

    public RuleCatalogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

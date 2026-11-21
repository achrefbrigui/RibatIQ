using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Domain.Tests;

/// <summary>Builds valid findings with overridable fields.</summary>
internal static class TestFindings
{
    /// <remarks>
    /// When <paramref name="origin"/> is omitted, a finding with a confidence score is an AI finding
    /// (deterministic findings never carry one) and a finding without is a static-analysis finding.
    /// </remarks>
    public static ReviewFinding Create(
        string title = "Possible null dereference",
        string description = "The value may be null here.",
        FindingSeverity severity = FindingSeverity.Medium,
        FindingCategory category = FindingCategory.Correctness,
        string filePath = "src/App/Service.cs",
        int? startLine = 10,
        int? endLine = 12,
        string? suggestedFix = null,
        double? confidence = null,
        FindingOrigin? origin = null,
        string source = "roslyn",
        string? toolRuleId = "CS8602",
        ReviewRuleId? ruleId = null,
        string? evidence = null) =>
        ReviewFinding.Create(
            title, description, severity, category, filePath, startLine, endLine,
            suggestedFix, confidence, origin ?? (confidence is null ? FindingOrigin.StaticAnalysis : FindingOrigin.Ai),
            source, toolRuleId, ruleId, evidence);
}

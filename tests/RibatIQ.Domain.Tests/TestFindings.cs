using RibatIQ.Domain.Findings;

namespace RibatIQ.Domain.Tests;

/// <summary>Builds valid findings with overridable fields.</summary>
internal static class TestFindings
{
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
        FindingOrigin origin = FindingOrigin.StaticAnalysis,
        string source = "roslyn",
        string? ruleId = "CS8602") =>
        ReviewFinding.Create(
            title, description, severity, category, filePath, startLine, endLine,
            suggestedFix, confidence, origin, source, ruleId);
}

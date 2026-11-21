using System.Text.Json;

using RibatIQ.Application.Configuration;
using RibatIQ.Application.Rules;

namespace RibatIQ.IntegrationTests;

/// <summary>Keeps <c>docs/review-rules.md</c> in line with the rule catalogue.</summary>
public sealed class RulesDocumentationTests
{
    private static readonly string[] TableRows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Docs", "review-rules.md"))
        .Where(l => l.StartsWith("| `", StringComparison.Ordinal))
        .ToArray();

    [Fact]
    public void EveryCatalogueRule_IsDocumented_WithItsClassification()
    {
        foreach (var rule in RuleCatalog.Default.Rules)
        {
            var row = Assert.Single(TableRows, r => r.StartsWith($"| `{rule.Id.Value}` |", StringComparison.Ordinal));
            var cells = row.Split('|', StringSplitOptions.TrimEntries)[1..^1];

            Assert.Equal(JsonNamingPolicy.CamelCase.ConvertName(rule.Category.ToString()), cells[1]);
            Assert.Equal(SeverityNames.Of(rule.DefaultSeverity), cells[2]);
            Assert.Equal(Detection(rule.Detection), cells[3]);
            Assert.Equal(rule.EnabledByDefault ? "yes" : "no", cells[4]);
            Assert.Equal(rule.Title, cells[5]);
        }
    }

    [Fact]
    public void EveryProfile_IsDocumented()
    {
        foreach (var profile in RuleCatalog.Default.Profiles)
        {
            Assert.Contains(TableRows, r => r.StartsWith($"| `{profile.Name}` |", StringComparison.Ordinal));
        }
    }

    private static string Detection(DetectionMethod detection) => detection switch
    {
        DetectionMethod.StaticAnalysis => "static-analysis",
        DetectionMethod.DependencyAudit => "dependency-audit",
        _ => "ai-review",
    };
}

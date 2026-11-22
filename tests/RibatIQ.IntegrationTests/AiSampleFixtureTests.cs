using System.Text.RegularExpressions;

using RibatIQ.Application.Rules;
using RibatIQ.Application.Sources;
using RibatIQ.Domain.Rules;
using RibatIQ.Infrastructure.Roslyn;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// Keeps <c>tests/Fixtures/AiSample</c> a fair target for the AI review: static analysis finds nothing
/// there, and every expected finding in its README points at a real line and an AI-review rule.
/// Whether a model finds them is checked by hand, not here.
/// </summary>
public sealed partial class AiSampleFixtureTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AiSample");

    [Fact]
    public async Task StaticAnalysis_FindsNothing_SoEveryFindingComesFromTheModel()
    {
        var documents = Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories)
            .Select(f => new SourceDocument(Path.GetRelativePath(Root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToArray();

        var result = await new RoslynStaticAnalyzer().AnalyzeAsync(documents, TestContext.Current.CancellationToken);

        Assert.Equal(4, documents.Length);
        Assert.Empty(result.Findings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ExpectedFindings_PointAtRealLines_AndAiReviewRulesEnabledByDefault()
    {
        var expected = File.ReadAllLines(Path.Combine(Root, "README.md"))
            .Select(line => ExpectedRow().Match(line))
            .Where(m => m.Success)
            .ToArray();

        Assert.Equal(11, expected.Length);

        foreach (var row in expected)
        {
            var file = Path.Combine(Root, row.Groups["file"].Value);
            Assert.True(File.Exists(file), $"Missing fixture file {row.Groups["file"].Value}.");

            var lines = File.ReadAllLines(file);
            var lineNumber = int.Parse(row.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(lineNumber, 1, lines.Length);
            Assert.False(string.IsNullOrWhiteSpace(lines[lineNumber - 1]), $"{row.Groups["file"].Value}:{lineNumber} is blank.");

            var rule = RuleCatalog.Default.Find(ReviewRuleId.Parse(row.Groups["rule"].Value));
            Assert.NotNull(rule);
            Assert.Equal(DetectionMethod.AiReview, rule.Detection);
            Assert.True(rule.EnabledByDefault, $"{rule.Id.Value} is not enabled by default.");
        }

        Assert.Equal(expected.Length, expected.Select(r => r.Groups["rule"].Value).Distinct(StringComparer.Ordinal).Count());
    }

    [GeneratedRegex(@"^\| `(?<file>[^`:]+):(?<line>\d+)` \| `(?<rule>[A-Z0-9_.]+)` \|")]
    private static partial Regex ExpectedRow();
}

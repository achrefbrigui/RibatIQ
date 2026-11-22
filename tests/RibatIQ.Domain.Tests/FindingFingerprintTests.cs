using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Domain.Tests;

public sealed class FindingFingerprintTests
{
    private static readonly ReviewRuleId Secret = ReviewRuleId.Parse("SECURITY.EXPOSED_SECRET");
    private static readonly ReviewRuleId Leak = ReviewRuleId.Parse("DOTNET.DISPOSAL.RESOURCE_LEAK");

    [Fact]
    public void Compute_IsDeterministic_AndWellFormed()
    {
        var first = FindingFingerprint.Compute("src/a.cs", Secret, "RIQ1004", "Title", 10);
        var second = FindingFingerprint.Compute("src/a.cs", Secret, "RIQ1004", "Title", 10);

        Assert.Equal(first, second);
        Assert.Equal(FindingFingerprint.Length, first.Length);
        Assert.True(FindingFingerprint.IsWellFormed(first));
        Assert.Equal("v2", FindingFingerprint.AlgorithmVersion);
    }

    // If one of these fails, the algorithm changed: bump FindingFingerprint.AlgorithmVersion and update
    // the values deliberately, since it invalidates existing baselines. Each value is the first 16 bytes
    // of SHA-256("v2\n<path>\n<rule key>\n<line>"), computed independently with sha256sum.
    [Theory]
    [InlineData("SECURITY.EXPOSED_SECRET", "RIQ1004", "Title", "c03084689268499913fda174a3ac7239")]
    [InlineData("SECURITY.EXPOSED_SECRET", null, "Title", "dae23c7d06d20391ac1e409c8596a8bf")]
    [InlineData(null, "CS8602", "Title", "3c86d1c2305073924073b7f857a30086")]
    [InlineData(null, null, "SQL  injection RISK", "1bdf5f28c463bb80128889bdb800c90b")]
    public void Compute_MatchesPinnedValues_SoIdsStayStableAcrossReleases(string? ruleId, string? toolRuleId, string title, string expected)
    {
        var rule = ruleId is null ? null : ReviewRuleId.Parse(ruleId);

        Assert.Equal(expected, FindingFingerprint.Compute("src/a.cs", rule, toolRuleId, title, 10));
    }

    [Fact]
    public void Compute_IsIndependentOfPathSeparatorAndLeadingDotSlash()
    {
        var expected = FindingFingerprint.Compute("src/App/a.cs", Leak, null, "T", 1);

        Assert.Equal(expected, FindingFingerprint.Compute(@"src\App\a.cs", Leak, null, "T", 1));
        Assert.Equal(expected, FindingFingerprint.Compute("./src/App/a.cs", Leak, null, "T", 1));
    }

    [Fact]
    public void Compute_WithRuleId_IgnoresTitle()
    {
        Assert.Equal(
            FindingFingerprint.Compute("a.cs", Leak, null, "The stream is never disposed", 3),
            FindingFingerprint.Compute("a.cs", Leak, null, "Different wording from another provider", 3));
    }

    [Fact]
    public void Compute_ToolRuleId_KeepsDistinctDiagnosticsOfOneRuleApart()
    {
        var syntax = ReviewRuleId.Parse("CSHARP.SYNTAX.ERROR");

        Assert.NotEqual(
            FindingFingerprint.Compute("a.cs", syntax, "CS1002", "T", 3),
            FindingFingerprint.Compute("a.cs", syntax, "CS1026", "T", 3));
    }

    [Fact]
    public void Compute_WithoutAnyRule_UsesNormalizedTitle()
    {
        Assert.Equal(
            FindingFingerprint.Compute("a.cs", null, null, "SQL  injection\trisk", 3),
            FindingFingerprint.Compute("a.cs", null, "  ", "sql injection RISK", 3));

        Assert.NotEqual(
            FindingFingerprint.Compute("a.cs", null, null, "SQL injection risk", 3),
            FindingFingerprint.Compute("a.cs", null, null, "Path traversal risk", 3));
    }

    [Fact]
    public void Compute_DistinguishesPathRuleKeyAndLine()
    {
        var baseline = FindingFingerprint.Compute("a.cs", Leak, null, "T", 1);

        Assert.NotEqual(baseline, FindingFingerprint.Compute("b.cs", Leak, null, "T", 1));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", Secret, null, "T", 1));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", Leak, "RIQ9999", "T", 1));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", null, null, "T", 1));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", Leak, null, "T", 2));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", Leak, null, "T", null));
    }

    [Fact]
    public void Finding_Id_IgnoresSeverityDescriptionEvidenceConfidenceAndSource()
    {
        var a = TestFindings.Create(severity: FindingSeverity.Low, description: "one", evidence: "e1", confidence: 0.1, source: "openai", toolRuleId: null, ruleId: Leak);
        var b = TestFindings.Create(severity: FindingSeverity.Critical, description: "two", evidence: "e2", confidence: 0.9, source: "anthropic", toolRuleId: null, ruleId: Leak);

        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public void Compute_WithoutAnyRuleAndBlankTitle_Throws() =>
        Assert.Throws<ArgumentException>(() => FindingFingerprint.Compute("a.cs", null, null, " ", 1));
}

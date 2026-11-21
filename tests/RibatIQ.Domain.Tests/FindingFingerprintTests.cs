using RibatIQ.Domain.Findings;

namespace RibatIQ.Domain.Tests;

public sealed class FindingFingerprintTests
{
    [Fact]
    public void Compute_IsDeterministic_AndWellFormed()
    {
        var first = FindingFingerprint.Compute("src/a.cs", "CS8602", "Title", 10);
        var second = FindingFingerprint.Compute("src/a.cs", "CS8602", "Title", 10);

        Assert.Equal(first, second);
        Assert.Equal(FindingFingerprint.Length, first.Length);
        Assert.True(FindingFingerprint.IsWellFormed(first));
    }

    [Fact]
    public void Compute_MatchesPinnedValue_SoIdsStayStableAcrossReleases()
    {
        // If this fails, the algorithm changed: bump FindingFingerprint.AlgorithmVersion
        // and update this value deliberately, since it invalidates existing baselines.
        var fingerprint = FindingFingerprint.Compute("src/a.cs", "CS8602", "Title", 10);

        Assert.Equal(PinnedFingerprint, fingerprint);
    }

    [Fact]
    public void Compute_IsIndependentOfPathSeparatorAndLeadingDotSlash()
    {
        var expected = FindingFingerprint.Compute("src/App/a.cs", "R1", "T", 1);

        Assert.Equal(expected, FindingFingerprint.Compute(@"src\App\a.cs", "R1", "T", 1));
        Assert.Equal(expected, FindingFingerprint.Compute("./src/App/a.cs", "R1", "T", 1));
    }

    [Fact]
    public void Compute_WithRuleId_IgnoresTitle()
    {
        Assert.Equal(
            FindingFingerprint.Compute("a.cs", "CS0168", "Variable declared but never used", 3),
            FindingFingerprint.Compute("a.cs", "CS0168", "Some other wording", 3));
    }

    [Fact]
    public void Compute_WithoutRuleId_UsesNormalizedTitle()
    {
        Assert.Equal(
            FindingFingerprint.Compute("a.cs", null, "SQL  injection\trisk", 3),
            FindingFingerprint.Compute("a.cs", "  ", "sql injection RISK", 3));

        Assert.NotEqual(
            FindingFingerprint.Compute("a.cs", null, "SQL injection risk", 3),
            FindingFingerprint.Compute("a.cs", null, "Path traversal risk", 3));
    }

    [Fact]
    public void Compute_DistinguishesPathRuleAndLine()
    {
        var baseline = FindingFingerprint.Compute("a.cs", "R1", "T", 1);

        Assert.NotEqual(baseline, FindingFingerprint.Compute("b.cs", "R1", "T", 1));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", "R2", "T", 1));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", "R1", "T", 2));
        Assert.NotEqual(baseline, FindingFingerprint.Compute("a.cs", "R1", "T", null));
    }

    [Fact]
    public void Finding_Id_IgnoresSeverityDescriptionConfidenceAndSource()
    {
        var a = TestFindings.Create(severity: FindingSeverity.Low, description: "one", confidence: 0.1, source: "openai");
        var b = TestFindings.Create(severity: FindingSeverity.Critical, description: "two", confidence: 0.9, source: "anthropic");

        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public void Compute_WithoutRuleIdAndBlankTitle_Throws() =>
        Assert.Throws<ArgumentException>(() => FindingFingerprint.Compute("a.cs", null, " ", 1));

    // SHA-256("v1\nsrc/a.cs\nrule:CS8602\n10"), first 16 bytes, computed independently with sha256sum.
    private const string PinnedFingerprint = "1779833ba0847cfbc2839d40d658462b";
}

using RibatIQ.Domain.Findings;

namespace RibatIQ.Domain.Tests;

public sealed class ReviewFindingTests
{
    [Fact]
    public void Create_WithValidInput_NormalizesAndKeepsValues()
    {
        var finding = TestFindings.Create(
            title: "  Possible null dereference  ",
            filePath: @".\src\App\Service.cs",
            suggestedFix: "   ",
            confidence: 0.8,
            origin: FindingOrigin.Ai,
            source: "openai",
            ruleId: null);

        Assert.Equal("Possible null dereference", finding.Title);
        Assert.Equal("src/App/Service.cs", finding.FilePath);
        Assert.Null(finding.SuggestedFix);
        Assert.Null(finding.RuleId);
        Assert.Equal(0.8, finding.Confidence);
        Assert.Equal(FindingOrigin.Ai, finding.Origin);
        Assert.Equal(10, finding.StartLine);
        Assert.Equal(12, finding.EndLine);
        Assert.True(FindingFingerprint.IsWellFormed(finding.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTitle_Throws(string title) =>
        Assert.Throws<ArgumentException>(() => TestFindings.Create(title: title));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithBlankDescriptionOrSource_Throws(string blank)
    {
        Assert.Throws<ArgumentException>(() => TestFindings.Create(description: blank));
        Assert.Throws<ArgumentException>(() => TestFindings.Create(source: blank));
    }

    [Fact]
    public void Create_WithOverlongTitle_Throws() =>
        Assert.Throws<ArgumentException>(() => TestFindings.Create(title: new string('x', ReviewFinding.MaxTitleLength + 1)));

    [Fact]
    public void Create_WithOverlongDescription_Throws() =>
        Assert.Throws<ArgumentException>(() => TestFindings.Create(description: new string('x', ReviewFinding.MaxTextLength + 1)));

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(10, 9)]
    public void Create_WithInvalidLineRange_Throws(int? startLine, int? endLine) =>
        Assert.ThrowsAny<ArgumentException>(() => TestFindings.Create(startLine: startLine, endLine: endLine));

    [Fact]
    public void Create_WithEndLineButNoStartLine_Throws() =>
        Assert.Throws<ArgumentException>(() => TestFindings.Create(startLine: null, endLine: 5));

    [Theory]
    [InlineData(null, null)]
    [InlineData(7, null)]
    [InlineData(7, 7)]
    public void Create_WithValidLineRange_Succeeds(int? startLine, int? endLine)
    {
        var finding = TestFindings.Create(startLine: startLine, endLine: endLine);

        Assert.Equal(startLine, finding.StartLine);
        Assert.Equal(endLine, finding.EndLine);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Create_WithConfidenceOutOfRange_Throws(double confidence) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TestFindings.Create(confidence: confidence));

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Create_WithConfidenceAtBounds_Succeeds(double confidence) =>
        Assert.Equal(confidence, TestFindings.Create(confidence: confidence).Confidence);

    [Fact]
    public void Create_WithUndefinedEnumValues_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestFindings.Create(severity: (FindingSeverity)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => TestFindings.Create(category: (FindingCategory)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => TestFindings.Create(origin: (FindingOrigin)42));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-fingerprint")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("abcdef0123456789abcdef012345678")]
    public void Constructor_WithMalformedId_Throws(string id) =>
        Assert.Throws<ArgumentException>(() => new ReviewFinding(
            id, "Title", "Description", FindingSeverity.Low, FindingCategory.Style, "a.cs",
            null, null, null, null, FindingOrigin.StaticAnalysis, "roslyn", null));

    [Fact]
    public void Constructor_WithExistingId_PreservesIt()
    {
        var original = TestFindings.Create();

        var roundTripped = new ReviewFinding(
            original.Id, original.Title, original.Description, original.Severity, original.Category,
            original.FilePath, original.StartLine, original.EndLine, original.SuggestedFix,
            original.Confidence, original.Origin, original.Source, original.RuleId);

        Assert.Equal(original, roundTripped);
    }
}

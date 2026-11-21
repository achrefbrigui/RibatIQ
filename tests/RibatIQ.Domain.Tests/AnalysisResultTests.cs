using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Results;

namespace RibatIQ.Domain.Tests;

public sealed class AnalysisResultTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly AnalysisMetadata NoAi = AnalysisMetadata.WithoutAi("0.1.0");

    [Fact]
    public void Constructor_OrdersFindingsBySeverityThenPathThenLine()
    {
        var low = TestFindings.Create(severity: FindingSeverity.Low, filePath: "a.cs", startLine: 1, endLine: null);
        var highB = TestFindings.Create(severity: FindingSeverity.High, filePath: "b.cs", startLine: 1, endLine: null);
        var highA20 = TestFindings.Create(severity: FindingSeverity.High, filePath: "a.cs", startLine: 20, endLine: null);
        var highA3 = TestFindings.Create(severity: FindingSeverity.High, filePath: "a.cs", startLine: 3, endLine: null);
        var critical = TestFindings.Create(severity: FindingSeverity.Critical, filePath: "z.cs", startLine: 1, endLine: null);

        var result = CreateResult([low, highB, highA20, highA3, critical]);

        Assert.Equal([critical, highA3, highA20, highB, low], result.Findings);
    }

    [Fact]
    public void Constructor_ComputesSummary()
    {
        var result = CreateResult(
        [
            TestFindings.Create(severity: FindingSeverity.Critical, startLine: 1, endLine: null),
            TestFindings.Create(severity: FindingSeverity.High, startLine: 2, endLine: null, origin: FindingOrigin.Ai, source: "openai"),
            TestFindings.Create(severity: FindingSeverity.High, startLine: 3, endLine: null),
            TestFindings.Create(severity: FindingSeverity.Info, startLine: 4, endLine: null),
        ]);

        Assert.Equal(new FindingSummary(Total: 4, Critical: 1, High: 2, Medium: 0, Low: 0, Info: 1, StaticAnalysis: 3, Ai: 1), result.Summary);
        Assert.Equal(1, result.Summary.CountAtOrAbove(FindingSeverity.Critical));
        Assert.Equal(3, result.Summary.CountAtOrAbove(FindingSeverity.High));
        Assert.Equal(3, result.Summary.CountAtOrAbove(FindingSeverity.Low));
        Assert.Equal(4, result.Summary.CountAtOrAbove(FindingSeverity.Info));
    }

    [Fact]
    public void Constructor_WithNoFindings_HasEmptySummary()
    {
        var result = CreateResult([]);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.Total);
    }

    [Fact]
    public void Constructor_WithDuplicateIds_Throws()
    {
        var finding = TestFindings.Create();
        var sameIssueDifferentSeverity = TestFindings.Create(severity: FindingSeverity.Critical);

        Assert.Throws<ArgumentException>(() => CreateResult([finding, sameIssueDifferentSeverity]));
    }

    [Fact]
    public void Constructor_WithNullFinding_Throws() =>
        Assert.Throws<ArgumentException>(() => CreateResult([TestFindings.Create(), null!]));

    [Fact]
    public void Constructor_WithCompletionBeforeStart_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new AnalysisResult(AnalysisKind.Scan, "MySolution.sln", [], NoAi, Start, Start.AddSeconds(-1)));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WithBlankTarget_Throws(string target) =>
        Assert.Throws<ArgumentException>(() =>
            new AnalysisResult(AnalysisKind.Review, target, [], NoAi, Start, Start));

    [Fact]
    public void Constructor_WithUndefinedKind_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnalysisResult((AnalysisKind)9, "x", [], NoAi, Start, Start));

    [Fact]
    public void Constructor_KeepsTimestampsAndCleansWarnings()
    {
        var result = new AnalysisResult(
            AnalysisKind.Review, " origin/main..HEAD ", [], NoAi, Start, Start.AddSeconds(5),
            ["  Context truncated  ", "", "   "]);

        Assert.Equal("origin/main..HEAD", result.Target);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
        Assert.Equal(["Context truncated"], result.Warnings);
    }

    [Fact]
    public void Metadata_WithAiEnabled_RequiresProvider() =>
        Assert.Throws<ArgumentException>(() => new AnalysisMetadata("0.1.0", aiEnabled: true, provider: " ", model: "m"));

    [Fact]
    public void Metadata_WithAiDisabled_RejectsProviderOrModel()
    {
        Assert.Throws<ArgumentException>(() => new AnalysisMetadata("0.1.0", aiEnabled: false, provider: "openai", model: null));
        Assert.Throws<ArgumentException>(() => new AnalysisMetadata("0.1.0", aiEnabled: false, provider: null, model: "m"));
    }

    [Fact]
    public void Metadata_WithAiEnabled_KeepsProviderAndModel()
    {
        var metadata = new AnalysisMetadata(" 0.1.0 ", aiEnabled: true, provider: " anthropic ", model: null);

        Assert.Equal("0.1.0", metadata.ToolVersion);
        Assert.Equal("anthropic", metadata.Provider);
        Assert.Null(metadata.Model);
    }

    private static AnalysisResult CreateResult(IEnumerable<ReviewFinding> findings) =>
        new(AnalysisKind.Scan, "MySolution.sln", findings, NoAi, Start, Start.AddSeconds(1));
}

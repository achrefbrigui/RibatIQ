using RibatIQ.Application.Analysis;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Tests;

public sealed class FindingDeduplicatorTests
{
    [Fact]
    public void Deduplicate_KeepsDistinctFindingsInOriginalOrder()
    {
        var a = Finding(line: 1);
        var b = Finding(line: 2);
        var c = Finding(line: 3);

        Assert.Equal([c, a, b], FindingDeduplicator.Deduplicate([c, a, b]));
    }

    [Fact]
    public void Deduplicate_PrefersStaticAnalysisOverAi()
    {
        var ai = Finding(line: 1, severity: FindingSeverity.Critical, origin: FindingOrigin.Ai);
        var roslyn = Finding(line: 1, severity: FindingSeverity.Low, origin: FindingOrigin.StaticAnalysis);

        var result = FindingDeduplicator.Deduplicate([ai, roslyn]);

        Assert.Equal([roslyn], result);
    }

    [Fact]
    public void Deduplicate_WithSameOrigin_KeepsHigherSeverity()
    {
        var low = Finding(line: 1, severity: FindingSeverity.Low);
        var high = Finding(line: 1, severity: FindingSeverity.High);

        Assert.Equal([high], FindingDeduplicator.Deduplicate([low, high]));
    }

    [Fact]
    public void Deduplicate_WithEqualCandidates_KeepsFirst()
    {
        var first = Finding(line: 1, description: "first");
        var second = Finding(line: 1, description: "second");

        Assert.Same(first, Assert.Single(FindingDeduplicator.Deduplicate([first, second])));
    }

    private static ReviewFinding Finding(
        int line,
        FindingSeverity severity = FindingSeverity.Medium,
        FindingOrigin origin = FindingOrigin.StaticAnalysis,
        string description = "Description") =>
        ReviewFinding.Create(
            "Title", description, severity, FindingCategory.Correctness, "src/a.cs", line, null,
            null, null, origin, "test", "R1");
}

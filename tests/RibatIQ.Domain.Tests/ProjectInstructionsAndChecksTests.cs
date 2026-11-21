using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Policies;
using RibatIQ.Domain.Results;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Domain.Tests;

public sealed class ProjectInstructionsAndChecksTests
{
    private static readonly ReviewRuleId Secret = ReviewRuleId.Parse("SECURITY.EXPOSED_SECRET");
    private static readonly ReviewRuleId AsyncVoid = ReviewRuleId.Parse("DOTNET.ASYNC.ASYNC_VOID");
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    [InlineData("\u0000\u0007")]
    public void Instructions_WithoutUsableText_AreAbsent(string? text) =>
        Assert.Null(ProjectInstructions.Create(text, "review-rules.md"));

    [Fact]
    public void Instructions_AreNormalized()
    {
        var instructions = ProjectInstructions.Create("  # Rules\r\n- Use\tResult<T>\u0000\u001b[31m\r\n  ", " review-rules.md ")!;

        Assert.Equal("# Rules\n- Use\tResult<T>[31m", instructions.Text);
        Assert.Equal("review-rules.md", instructions.Source);
        Assert.False(instructions.Truncated);
    }

    [Fact]
    public void Instructions_AreCappedAtTheMaximumLength()
    {
        var instructions = ProjectInstructions.Create(new string('x', ProjectInstructions.MaxLength + 500), "review-rules.md")!;

        Assert.Equal(ProjectInstructions.MaxLength, instructions.Text.Length);
        Assert.True(instructions.Truncated);
    }

    [Fact]
    public void Instructions_TruncationNeverSplitsASurrogatePair()
    {
        var text = new string('x', ProjectInstructions.MaxLength - 1) + "😀" + "tail";

        var instructions = ProjectInstructions.Create(text, "review-rules.md")!;

        Assert.True(instructions.Truncated);
        Assert.False(char.IsHighSurrogate(instructions.Text[^1]));
    }

    [Fact]
    public void Instructions_RequireASource() =>
        Assert.Throws<ArgumentException>(() => ProjectInstructions.Create("text", " "));

    [Fact]
    public void Check_Completed_NeedsNoReason_AndSortsItsRules()
    {
        var check = CheckResult.Completed("roslyn.rules", "RibatIQ rules", [Secret, AsyncVoid, Secret]);

        Assert.Equal(CheckStatus.Completed, check.Status);
        Assert.Null(check.Reason);
        Assert.Equal([AsyncVoid, Secret], check.RuleIds);
    }

    [Fact]
    public void Check_SkippedOrFailed_MustSayWhy()
    {
        Assert.Throws<ArgumentException>(() => new CheckResult("nuget.audit", "NuGet audit", CheckStatus.Skipped));
        Assert.Throws<ArgumentException>(() => new CheckResult("ai.review", "AI review", CheckStatus.Failed, "  "));

        var skipped = CheckResult.Skipped("nuget.audit", "NuGet audit", " Run dotnet restore first. ");
        Assert.Equal("Run dotnet restore first.", skipped.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Roslyn.Rules")]
    [InlineData("1check")]
    [InlineData("roslyn rules")]
    public void Check_InvalidId_Throws(string checkId) =>
        Assert.Throws<ArgumentException>(() => CheckResult.Completed(checkId, "Name"));

    [Fact]
    public void Check_OverlongReason_Throws() =>
        Assert.Throws<ArgumentException>(() => CheckResult.Failed("ai.review", "AI", new string('r', CheckResult.MaxReasonLength + 1)));

    [Fact]
    public void Result_DistinguishesCompletedSkippedAndFailedChecks()
    {
        var secret = TestFindings.Create(severity: FindingSeverity.High, toolRuleId: "RIQ1004", ruleId: Secret);
        var rules = CheckResult.Completed("roslyn.rules", "RibatIQ rules", [Secret, AsyncVoid]);
        var audit = CheckResult.Skipped("nuget.audit", "NuGet audit", "Not requested.");

        var partial = new AnalysisResult(
            AnalysisKind.Scan, "App.sln", [secret], AnalysisMetadata.WithoutAi("1.0.0"), Start, Start, checks: [rules, audit]);
        var complete = new AnalysisResult(
            AnalysisKind.Scan, "App.sln", [], AnalysisMetadata.WithoutAi("1.0.0"), Start, Start, checks: [rules]);

        Assert.Equal([rules, audit], partial.Checks);
        Assert.False(partial.AllChecksCompleted);
        Assert.Equal(1, partial.CountFindings(rules));
        Assert.Equal(0, partial.CountFindings(audit));

        // Completed with no finding is "checked, no issue", which is different from "not checked".
        Assert.True(complete.AllChecksCompleted);
        Assert.Equal(0, complete.CountFindings(rules));
    }

    [Fact]
    public void Result_WithDuplicateCheckIds_Throws() =>
        Assert.Throws<ArgumentException>(() => new AnalysisResult(
            AnalysisKind.Scan, "App.sln", [], AnalysisMetadata.WithoutAi("1.0.0"), Start, Start,
            checks: [CheckResult.Completed("roslyn.rules", "A"), CheckResult.Completed("roslyn.rules", "B")]));

    [Fact]
    public void Result_WithoutChecks_HasNoneAndIsTriviallyComplete()
    {
        var result = new AnalysisResult(AnalysisKind.Scan, "App.sln", [], AnalysisMetadata.WithoutAi("1.0.0"), Start, Start);

        Assert.Empty(result.Checks);
        Assert.True(result.AllChecksCompleted);
    }
}

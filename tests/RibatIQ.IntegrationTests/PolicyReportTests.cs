using System.Text.Json;

using RibatIQ.Application.Instructions;
using RibatIQ.Application.Reporting;
using RibatIQ.Application.Rules;
using RibatIQ.Cli.Output;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Policies;
using RibatIQ.Domain.Results;
using RibatIQ.Domain.Rules;
using RibatIQ.Infrastructure.Reporting;

namespace RibatIQ.IntegrationTests;

/// <summary>How reports show the review policy, the project instructions and incomplete analysis.</summary>
public sealed class PolicyReportTests
{
    private const string InstructionsText = "Prefer records for DTOs. SECRET-INSTRUCTION-TEXT";

    private static readonly DateTimeOffset Start = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReviewRuleId SecretRule = ReviewRuleId.Parse("SECURITY.EXPOSED_SECRET");

    private static readonly CheckResult StaticCompleted = CheckResult.Completed("static-analysis", "Static analysis", [SecretRule]);
    private static readonly CheckResult AiCompleted = CheckResult.Completed("ai-review", "AI review", [ReviewRuleId.Parse("SECURITY.SQL_INJECTION")]);
    private static readonly CheckResult AiSkipped = CheckResult.Skipped("ai-review", "AI review", "AI analysis is not enabled; choose a provider to enable it.");
    private static readonly CheckResult AuditFailed = CheckResult.Failed("dependency-audit", "NuGet vulnerability audit", "The dotnet CLI was not found on the PATH.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Json_DescribesThePolicy()
    {
        using var json = await RenderJsonAsync(CreateReport([StaticCompleted], policy: Policy("security", FindingSeverity.Medium), excluded: 3));
        var policy = json.RootElement.GetProperty("policy");

        Assert.Equal("security", policy.GetProperty("profile").GetString());
        Assert.Equal("medium", policy.GetProperty("minimumSeverity").GetString());
        Assert.Equal(3, policy.GetProperty("findingsExcluded").GetInt32());
        Assert.Equal(["dependencies", "security"], policy.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).Order(StringComparer.Ordinal));

        var enabled = policy.GetProperty("enabledRules").EnumerateArray().Select(r => r.GetString()!).ToArray();
        Assert.Contains("SECURITY.EXPOSED_SECRET", enabled);
        Assert.Contains("DEPENDENCY.NUGET.VULNERABLE", enabled);
        Assert.Equal(enabled.Order(StringComparer.Ordinal), enabled);
        Assert.Empty(policy.GetProperty("disabledRules").EnumerateArray());
    }

    [Fact]
    public async Task Json_DefaultProfile_SelectsEveryCategory()
    {
        using var json = await RenderJsonAsync(CreateReport([StaticCompleted], policy: Policy("default")));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("policy").GetProperty("categories").ValueKind);
    }

    [Fact]
    public async Task Json_WithoutPolicyOrInstructions_HasNulls()
    {
        using var json = await RenderJsonAsync(CreateReport([StaticCompleted]));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("policy").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("instructions").ValueKind);
    }

    [Fact]
    public async Task Json_IsComplete_OnlyWhenEveryCheckCompleted()
    {
        using var complete = await RenderJsonAsync(CreateReport([StaticCompleted, AiCompleted]));
        using var skipped = await RenderJsonAsync(CreateReport([StaticCompleted, AiSkipped]));
        using var failed = await RenderJsonAsync(CreateReport([StaticCompleted, AiCompleted, AuditFailed]));

        Assert.True(complete.RootElement.GetProperty("complete").GetBoolean());
        Assert.False(skipped.RootElement.GetProperty("complete").GetBoolean());
        Assert.False(failed.RootElement.GetProperty("complete").GetBoolean());

        var check = failed.RootElement.GetProperty("checks")[2];
        Assert.Equal("failed", check.GetProperty("status").GetString());
        Assert.Equal("The dotnet CLI was not found on the PATH.", check.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Json_DescribesInstructions_WithoutTheirText()
    {
        var report = CreateReport([StaticCompleted, AiCompleted], instructions: Loaded());

        using var writer = new StringWriter();
        await new JsonReportWriter().WriteAsync(report, writer, Ct);
        using var json = JsonDocument.Parse(writer.ToString());
        var instructions = json.RootElement.GetProperty("instructions");

        Assert.Equal("loaded", instructions.GetProperty("status").GetString());
        Assert.Equal("review-rules.md", instructions.GetProperty("source").GetString());
        Assert.DoesNotContain("SECRET-INSTRUCTION-TEXT", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_ShowsPolicyAndEveryCheck()
    {
        var text = await RenderConsoleAsync(CreateReport([StaticCompleted, AiSkipped, AuditFailed], policy: Policy("security", FindingSeverity.High)));

        Assert.Matches(@"Policy: profile security, \d+ rule\(s\) enabled, minimum severity high\.", text);
        Assert.Contains("  completed Static analysis (1 rule(s), 1 finding(s))", text, StringComparison.Ordinal);
        Assert.Contains("  skipped   AI review: AI analysis is not enabled", text, StringComparison.Ordinal);
        Assert.Contains("  FAILED    NuGet vulnerability audit: The dotnet CLI was not found on the PATH.", text, StringComparison.Ordinal);
        Assert.Contains("Incomplete analysis: AI review, NuGet vulnerability audit did not run or failed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_CompleteAnalysis_HasNoIncompleteNotice()
    {
        var text = await RenderConsoleAsync(CreateReport([StaticCompleted, AiCompleted]));

        Assert.Contains("Checks:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Incomplete analysis", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_SaysWhetherInstructionsWereUsed()
    {
        var used = await RenderConsoleAsync(CreateReport([StaticCompleted, AiCompleted], instructions: Loaded()));
        var unused = await RenderConsoleAsync(CreateReport([StaticCompleted, AiSkipped], instructions: Loaded()));
        var none = await RenderConsoleAsync(CreateReport(
            [StaticCompleted, AiCompleted],
            instructions: ProjectInstructionsLoader.Load(new InstructionsReadResult("review-rules.md", null, InstructionsStatus.NotFound))));

        Assert.Contains("Project instructions: review-rules.md (sent to the AI review as untrusted context).", used, StringComparison.Ordinal);
        Assert.Contains("Project instructions: review-rules.md found but not used, because the AI review did not run.", unused, StringComparison.Ordinal);
        Assert.DoesNotContain("Project instructions", none, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-INSTRUCTION-TEXT", used, StringComparison.Ordinal);
    }

    private static ReviewPolicy Policy(string profile, FindingSeverity minimum = FindingSeverity.Info) =>
        ReviewPolicyResolver.Resolve(RuleCatalog.Default, profile, minimumSeverity: minimum);

    private static InstructionsLoadResult Loaded() =>
        ProjectInstructionsLoader.Load(new InstructionsReadResult("review-rules.md", InstructionsText));

    private static async Task<JsonDocument> RenderJsonAsync(AnalysisReport report)
    {
        using var writer = new StringWriter();
        await new JsonReportWriter().WriteAsync(report, writer, Ct);
        return JsonDocument.Parse(writer.ToString());
    }

    private static async Task<string> RenderConsoleAsync(AnalysisReport report)
    {
        using var writer = new StringWriter();
        await new ConsoleReportWriter().WriteAsync(report, writer, Ct);
        return writer.ToString();
    }

    private static AnalysisReport CreateReport(
        CheckResult[] checks, ReviewPolicy? policy = null, int excluded = 0, InstructionsLoadResult? instructions = null)
    {
        var secret = ReviewFinding.Create(
            "Possible hard-coded secret", "'ApiKey' looks like a secret.", FindingSeverity.High, FindingCategory.Security,
            "src/Config.cs", 3, null, null, null, FindingOrigin.StaticAnalysis, "ribatiq-rules", "RIQ1004", SecretRule);

        var result = new AnalysisResult(
            AnalysisKind.Scan, "src", [secret], AnalysisMetadata.WithoutAi("1.2.3"), Start, Start.AddSeconds(1), [], checks);

        return new AnalysisReport(result, "src", "test coverage", new ReportStatistics(1, ProjectCount: 1), Ai: null)
        {
            Policy = policy,
            PolicyExcluded = excluded,
            Instructions = instructions,
        };
    }
}

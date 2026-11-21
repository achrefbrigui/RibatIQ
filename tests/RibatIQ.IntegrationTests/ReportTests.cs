using System.Text.Json;

using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;
using RibatIQ.Application.Reporting;
using RibatIQ.Cli;
using RibatIQ.Cli.Output;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;
using RibatIQ.Domain.Results;
using RibatIQ.Infrastructure.Reporting;

namespace RibatIQ.IntegrationTests;

/// <summary>Report contents (JSON and console), file publishing and the failure threshold.</summary>
public sealed class ReportTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReviewRuleId SecretRule = ReviewRuleId.Parse("SECURITY.EXPOSED_SECRET");

    private readonly TempDirectory _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Json_FollowsSchemaVersion2()
    {
        using var json = await RenderJsonAsync(CreateReport(FailPolicy.AtOrAbove(FindingSeverity.High), withAi: true));
        var root = json.RootElement;

        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("RibatIQ", root.GetProperty("tool").GetProperty("name").GetString());
        Assert.Equal("1.2.3", root.GetProperty("tool").GetProperty("version").GetString());
        Assert.Equal("review", root.GetProperty("kind").GetString());
        Assert.Equal("main...HEAD", root.GetProperty("target").GetString());
        Assert.Equal(1500, root.GetProperty("durationMs").GetInt64());
        Assert.Equal("test coverage", root.GetProperty("coverage").GetString());

        var ai = root.GetProperty("ai");
        Assert.True(ai.GetProperty("enabled").GetBoolean());
        Assert.Equal("anthropic", ai.GetProperty("provider").GetString());
        Assert.Equal("claude-opus-5-5", ai.GetProperty("model").GetString());
        Assert.Equal(2, ai.GetProperty("filesSent").GetInt32());
        Assert.Equal(1000, ai.GetProperty("inputTokens").GetInt64());

        var statistics = root.GetProperty("statistics");
        Assert.Equal(2, statistics.GetProperty("filesAnalyzed").GetInt32());
        Assert.Equal(5, statistics.GetProperty("changedFiles").GetInt32());
        Assert.Equal(JsonValueKind.Null, statistics.GetProperty("projects").ValueKind);

        var summary = root.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("total").GetInt32());
        Assert.Equal(1, summary.GetProperty("staticAnalysis").GetInt32());
        Assert.Equal(1, summary.GetProperty("ai").GetInt32());

        var gate = root.GetProperty("gate");
        Assert.Equal("high", gate.GetProperty("failOn").GetString());
        Assert.Equal(1, gate.GetProperty("failingFindings").GetInt32());
        Assert.True(gate.GetProperty("thresholdExceeded").GetBoolean());

        Assert.Equal(["Context truncated"], root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));

        var checks = root.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(2, checks.Length);
        Assert.Equal("roslyn.rules", checks[0].GetProperty("id").GetString());
        Assert.Equal("completed", checks[0].GetProperty("status").GetString());
        Assert.Equal(["SECURITY.EXPOSED_SECRET"], checks[0].GetProperty("rules").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(1, checks[0].GetProperty("findings").GetInt32());
        Assert.Equal("skipped", checks[1].GetProperty("status").GetString());
        Assert.Equal("Not requested.", checks[1].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Json_FindingsCarryStableIdsOriginAndLocation()
    {
        using var json = await RenderJsonAsync(CreateReport(null, withAi: true));
        var findings = json.RootElement.GetProperty("findings").EnumerateArray().ToArray();

        var roslyn = findings[0];
        Assert.True(FindingFingerprint.IsWellFormed(roslyn.GetProperty("id").GetString()));
        Assert.Equal("high", roslyn.GetProperty("severity").GetString());
        Assert.Equal("security", roslyn.GetProperty("category").GetString());
        Assert.Equal("staticAnalysis", roslyn.GetProperty("origin").GetString());
        Assert.Equal("SECURITY.EXPOSED_SECRET", roslyn.GetProperty("ruleId").GetString());
        Assert.Equal("RIQ1004", roslyn.GetProperty("toolRuleId").GetString());
        Assert.Equal("String literal assigned to ApiKey.", roslyn.GetProperty("evidence").GetString());
        Assert.Equal("src/My App/Config.cs", roslyn.GetProperty("file").GetString());
        Assert.Equal(3, roslyn.GetProperty("startLine").GetInt32());
        Assert.Equal(JsonValueKind.Null, roslyn.GetProperty("confidence").ValueKind);

        var ai = findings[1];
        Assert.Equal("ai", ai.GetProperty("origin").GetString());
        Assert.Equal("anthropic", ai.GetProperty("source").GetString());
        Assert.Equal(0.7, ai.GetProperty("confidence").GetDouble());
        Assert.Equal("Return Task.", ai.GetProperty("suggestedFix").GetString());
        Assert.Equal(JsonValueKind.Null, ai.GetProperty("ruleId").ValueKind);
        Assert.Equal(JsonValueKind.Null, ai.GetProperty("toolRuleId").ValueKind);
    }

    [Fact]
    public async Task Json_WithoutAiOrThreshold_HasExplicitDefaults()
    {
        using var json = await RenderJsonAsync(CreateReport(null, withAi: false));
        var root = json.RootElement;

        Assert.False(root.GetProperty("ai").GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ai").GetProperty("provider").ValueKind);
        Assert.Equal("none", root.GetProperty("gate").GetProperty("failOn").GetString());
        Assert.False(root.GetProperty("gate").GetProperty("thresholdExceeded").GetBoolean());
    }

    [Fact]
    public async Task Json_KeepsTextReadable()
    {
        using var writer = new StringWriter();
        await new JsonReportWriter().WriteAsync(CreateReport(null, withAi: false), writer, Ct);

        Assert.Contains("'ApiKey' looks like a secret", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_ShowsConfidenceSuggestedFixAndThresholdOutcome()
    {
        using var writer = new StringWriter();

        await new ConsoleReportWriter().WriteAsync(CreateReport(FailPolicy.AtOrAbove(FindingSeverity.Critical), withAi: true), writer, Ct);

        var text = writer.ToString();
        Assert.Contains("RibatIQ review: main...HEAD", text, StringComparison.Ordinal);
        Assert.Contains("Reviewed 2 changed C# file(s) of 5 changed file(s); 3 finding(s) outside changed lines ignored.", text, StringComparison.Ordinal);
        Assert.Contains("AI: anthropic (claude-opus-5-5), 2 file(s) sent, 1000 input / 100 output tokens.", text, StringComparison.Ordinal);
        Assert.Contains("(confidence 0.70)", text, StringComparison.Ordinal);
        Assert.Contains("Suggested fix: Return Task.", text, StringComparison.Ordinal);
        Assert.Contains("Result: PASSED - no finding at or above 'critical'", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_WithoutThreshold_HasNoResultLine()
    {
        using var writer = new StringWriter();

        await new ConsoleReportWriter().WriteAsync(CreateReport(null, withAi: false), writer, Ct);

        Assert.DoesNotContain("Result:", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none", 0, false)]
    [InlineData("critical", 0, false)]
    [InlineData("high", 1, true)]
    [InlineData("medium", 2, true)]
    [InlineData("info", 2, true)]
    public void Threshold_CountsFindingsAtOrAboveIt(string failOn, int failing, bool exceeded)
    {
        Assert.True(FailPolicy.TryParse(failOn, out var policy));

        var report = CreateReport(policy, withAi: true);

        Assert.Equal(failing, report.FailingFindings);
        Assert.Equal(exceeded, report.ThresholdExceeded);
    }

    [Fact]
    public async Task Publish_ToFile_WritesTheReportAtomically_AndOverwritesAPreviousReport()
    {
        var path = _workspace.WriteFile("reports/ribatiq.json", "old content");

        await ReportPublisher.PublishAsync(CreateReport(null, withAi: false), "json", "reports/ribatiq.json", Context(), Ct);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, Ct));
        Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(["ribatiq.json"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("src/Program.cs")]
    [InlineData("App.csproj")]
    [InlineData("App.sln")]
    [InlineData("Directory.Build.props")]
    [InlineData(".github/workflows/ci.yml")]
    public async Task Publish_NeverOverwritesSourceOrBuildFiles(string target)
    {
        var path = _workspace.WriteFile(target, "original");

        await Assert.ThrowsAsync<RibatIQ.Application.InvalidInputException>(() =>
            ReportPublisher.PublishAsync(CreateReport(null, withAi: false), "console", target, Context(), Ct));

        Assert.Equal("original", await File.ReadAllTextAsync(path, Ct));
    }

    [Theory]
    [InlineData("missing-dir/report.json")]
    [InlineData(".")]
    public async Task Publish_ToInvalidLocation_IsInvalidInput(string target) =>
        await Assert.ThrowsAsync<RibatIQ.Application.InvalidInputException>(() =>
            ReportPublisher.PublishAsync(CreateReport(null, withAi: false), "json", target, Context(), Ct));

    [Theory]
    [InlineData("src/App.sln", "src/App.sln")]
    [InlineData(".", ".")]
    public void DisplayPath_IsRelativeInsideTheWorkingDirectory(string relative, string expected) =>
        Assert.Equal(expected, ReportPublisher.DisplayPath(Path.GetFullPath(Path.Combine(_workspace.Path, relative)), _workspace.Path));

    [Fact]
    public void DisplayPath_OutsideTheWorkingDirectory_IsAbsolute()
    {
        var outside = Path.GetFullPath(Path.Combine(_workspace.Path, "..", "elsewhere"));

        Assert.Equal(outside, ReportPublisher.DisplayPath(outside, _workspace.Path));
    }

    private CliContext Context() => new(TextWriter.Null, TextWriter.Null, _workspace.Path, _ => null);

    private static async Task<JsonDocument> RenderJsonAsync(AnalysisReport report)
    {
        using var writer = new StringWriter();
        await new JsonReportWriter().WriteAsync(report, writer, Ct);
        return JsonDocument.Parse(writer.ToString());
    }

    private static AnalysisReport CreateReport(FailPolicy? failPolicy, bool withAi)
    {
        var secret = ReviewFinding.Create(
            "Possible hard-coded secret", "'ApiKey' looks like a secret.", FindingSeverity.High, FindingCategory.Security,
            "src/My App/Config.cs", 3, null, null, null, FindingOrigin.StaticAnalysis, "ribatiq-rules", "RIQ1004",
            SecretRule, evidence: "String literal assigned to ApiKey.");

        var findings = new List<ReviewFinding> { secret };
        AiAnalysisResult? ai = null;
        var metadata = AnalysisMetadata.WithoutAi("1.2.3");

        if (withAi)
        {
            findings.Add(ReviewFinding.Create(
                "Async void", "Exceptions are unobserved.", FindingSeverity.Medium, FindingCategory.BestPractices,
                "src/Service.cs", 8, null, "Return Task.", 0.7, FindingOrigin.Ai, "anthropic", null));
            ai = new AiAnalysisResult([findings[1]], [], "anthropic", "claude-opus-5-5", 2, new LlmUsage(1000, 100));
            metadata = new AnalysisMetadata("1.2.3", aiEnabled: true, "anthropic", "claude-opus-5-5");
        }

        var result = new AnalysisResult(
            AnalysisKind.Review, "main...HEAD", findings, metadata, Start, Start.AddMilliseconds(1500), ["Context truncated"],
            [
                CheckResult.Completed("roslyn.rules", "RibatIQ rules", [SecretRule]),
                CheckResult.Skipped("nuget.audit", "NuGet vulnerability audit", "Not requested."),
            ]);

        return new AnalysisReport(
            result,
            "main...HEAD",
            "test coverage",
            new ReportStatistics(2, ChangedFileCount: 5, FindingsOutsideChanges: 3),
            ai,
            failPolicy);
    }
}

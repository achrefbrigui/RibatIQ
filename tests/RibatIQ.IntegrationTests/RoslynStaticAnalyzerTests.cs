using RibatIQ.Application.Sources;
using RibatIQ.Domain.Findings;
using RibatIQ.Infrastructure.Roslyn;

namespace RibatIQ.IntegrationTests;

public sealed class RoslynStaticAnalyzerTests
{
    [Fact]
    public async Task Analyze_CleanCode_HasNoFindings()
    {
        var result = await AnalyzeAsync("""
            public sealed class Clean
            {
                public async System.Threading.Tasks.Task RunAsync() => await System.Threading.Tasks.Task.Yield();
            }
            """);

        Assert.Empty(result.Findings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Analyze_NoDocuments_HasNoFindings() =>
        Assert.Empty((await new RoslynStaticAnalyzer().AnalyzeAsync([], TestContext.Current.CancellationToken)).Findings);

    [Fact]
    public async Task Analyze_SyntaxError_ReportsCompilerDiagnostic()
    {
        var result = await AnalyzeAsync("""
            public class Broken
            {
                public void M( { }
            }
            """);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("CS1026", finding.RuleId);
        Assert.Equal(3, finding.StartLine);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.Correctness, finding.Category);
        Assert.Equal(FindingOrigin.StaticAnalysis, finding.Origin);
        Assert.Equal(RoslynStaticAnalyzer.CompilerSource, finding.Source);
        Assert.Equal("src/Test.cs", finding.FilePath);
    }

    [Fact]
    public async Task Analyze_MissingReferences_AreNotReportedAsFindings()
    {
        // Semantic errors (unknown types) are expected without project references and must not be reported.
        var result = await AnalyzeAsync("""
            using Newtonsoft.Json;
            public class UsesPackage { public JObject Value { get; } = new JObject(); }
            """);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Analyze_EmptyCatch_ReportsRiq1001()
    {
        var result = await AnalyzeAsync("""
            public class C
            {
                public void M()
                {
                    try { M(); }
                    catch (System.Exception) { }
                }
            }
            """);

        AssertSingleRule(result.Findings, "RIQ1001", line: 6, FindingSeverity.Medium, FindingCategory.Reliability);
    }

    [Fact]
    public async Task Analyze_CatchWithCommentOrStatements_IsNotReported()
    {
        var result = await AnalyzeAsync("""
            public class C
            {
                public void M()
                {
                    try { M(); } catch (System.Exception) { /* optional cleanup: safe to ignore */ }
                    try { M(); } catch (System.Exception) {
                        // intentionally ignored
                    }
                    try { M(); } catch (System.Exception) { System.Console.WriteLine("x"); }
                }
            }
            """);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Analyze_AsyncVoid_ReportsRiq1002_ExceptForEventHandlers()
    {
        var result = await AnalyzeAsync("""
            public class C
            {
                public async void Fire() { await System.Threading.Tasks.Task.Yield(); }
                public async void OnClick(object sender, System.EventArgs e) { await System.Threading.Tasks.Task.Yield(); }
                public async void OnLoaded(object sender, RoutedEventArgs e) { await System.Threading.Tasks.Task.Yield(); }
                public void Outer()
                {
                    async void Local() { await System.Threading.Tasks.Task.Yield(); }
                    Local();
                }
            }
            """);

        Assert.Equal(["RIQ1002", "RIQ1002"], result.Findings.Select(f => f.RuleId));
        Assert.Equal([3, 8], result.Findings.Select(f => f.StartLine));
        Assert.Contains("'Fire'", result.Findings[0].Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ThrowCaughtVariable_ReportsRiq1003_ButNotBareRethrowOrNewException()
    {
        var result = await AnalyzeAsync("""
            public class C
            {
                public void M()
                {
                    try { M(); } catch (System.Exception ex) { throw ex; }
                    try { M(); } catch (System.Exception) { throw; }
                    try { M(); } catch (System.Exception ex) { throw new System.InvalidOperationException("x", ex); }
                    var other = new System.Exception();
                    try { M(); } catch (System.Exception) { throw other; }
                }
            }
            """);

        AssertSingleRule(result.Findings, "RIQ1003", line: 5, FindingSeverity.Low, FindingCategory.Reliability);
    }

    [Fact]
    public async Task Analyze_HardcodedSecret_ReportsRiq1004_WithoutLeakingTheValue()
    {
        var result = await AnalyzeAsync("""
            public class C
            {
                private const string ApiKey = "sk-live-0123456789";
                private string db_password = "hunter2-hunter2";
                public string ClientSecret { get; } = "abcdefgh12345";
                public void M(Options o) { o.AccessToken = "tok_0123456789"; }
            }
            """);

        Assert.All(result.Findings, f => Assert.Equal("RIQ1004", f.RuleId));
        Assert.Equal([3, 4, 5, 6], result.Findings.Select(f => f.StartLine).Order());
        Assert.All(result.Findings, f =>
        {
            Assert.Equal(FindingSeverity.High, f.Severity);
            Assert.Equal(FindingCategory.Security, f.Category);
            Assert.DoesNotContain("sk-live", f.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", f.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdefgh", f.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("tok_", f.Description, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Analyze_SecretLikeNamesWithoutSecretValues_AreNotReported()
    {
        var result = await AnalyzeAsync("""
            public class C
            {
                private const string TokenUrl = "https://example.com/token";
                private const string PasswordHeaderName = "X-Password-Header";
                private string password = "";
                private string token = "short";
                private string apiKey = "has spaces in it";
                private string Secret => System.Environment.GetEnvironmentVariable("SECRET");
                private string accessToken = $"Bearer {System.Guid.NewGuid()}";
            }
            """);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Analyze_FindingsHaveStableIds_AcrossRuns()
    {
        const string code = "public class C { public void M() { try { } catch { } } }";

        var first = await AnalyzeAsync(code);
        var second = await AnalyzeAsync(code);

        Assert.Equal(first.Findings.Select(f => f.Id), second.Findings.Select(f => f.Id));
    }

    private static void AssertSingleRule(
        IReadOnlyList<ReviewFinding> findings, string ruleId, int line, FindingSeverity severity, FindingCategory category)
    {
        var finding = Assert.Single(findings);
        Assert.Equal(ruleId, finding.RuleId);
        Assert.Equal(line, finding.StartLine);
        Assert.Equal(severity, finding.Severity);
        Assert.Equal(category, finding.Category);
        Assert.Equal(RibatRules.Source, finding.Source);
        Assert.Equal(FindingOrigin.StaticAnalysis, finding.Origin);
    }

    private static Task<Application.Analysis.StaticAnalysisResult> AnalyzeAsync(string code) =>
        new RoslynStaticAnalyzer().AnalyzeAsync([new SourceDocument("src/Test.cs", code)], TestContext.Current.CancellationToken);
}

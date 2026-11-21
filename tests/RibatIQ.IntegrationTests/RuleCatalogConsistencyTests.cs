using RibatIQ.Application.Rules;
using RibatIQ.Application.Sources;
using RibatIQ.Infrastructure.Roslyn;

namespace RibatIQ.IntegrationTests;

/// <summary>The catalogue and the detectors must agree: no rule without a detector, no detector without a rule.</summary>
public sealed class RuleCatalogConsistencyTests
{
    [Fact]
    public void EveryRoslynRule_IsAStaticAnalysisRuleInTheCatalogue() =>
        Assert.All(RoslynStaticAnalyzer.SupportedRules, id =>
        {
            var rule = RuleCatalog.Default.Find(id);
            Assert.NotNull(rule);
            Assert.Equal(DetectionMethod.StaticAnalysis, rule.Detection);
        });

    [Fact]
    public void EveryStaticAnalysisRuleInTheCatalogue_HasARoslynDetector() =>
        Assert.Equal(
            RuleCatalog.Default.RulesDetectedBy(DetectionMethod.StaticAnalysis).Select(r => r.Id).ToHashSet(),
            RoslynStaticAnalyzer.SupportedRules.ToHashSet());

    [Fact]
    public async Task RoslynFindings_TakeTheirClassificationFromTheCatalogue()
    {
        const string code = """
            public class C
            {
                private const string ApiKey = "sk-live-0123456789";
                public async void Fire() { await System.Threading.Tasks.Task.Yield(); }
                public void M()
                {
                    try { M(); } catch { }
                    try { M(); } catch (System.Exception ex) { throw ex; }
                }
                public void Broken( { }
            }
            """;

        var result = await new RoslynStaticAnalyzer().AnalyzeAsync([new SourceDocument("src/C.cs", code)], TestContext.Current.CancellationToken);

        Assert.Equal(
            ["CSHARP.SYNTAX.ERROR", "DOTNET.ASYNC.ASYNC_VOID", "DOTNET.EXCEPTIONS.EMPTY_CATCH", "DOTNET.EXCEPTIONS.RETHROW_LOSES_STACK", "SECURITY.EXPOSED_SECRET"],
            result.Findings.Select(f => f.RuleId!.Value).Distinct().Order(StringComparer.Ordinal));

        Assert.All(result.Findings, f =>
        {
            var rule = RuleCatalog.Default.Get(f.RuleId!);
            Assert.Equal(rule.Title, f.Title);
            Assert.Equal(rule.DefaultSeverity, f.Severity);
            Assert.Equal(rule.Category, f.Category);
            Assert.NotNull(f.ToolRuleId);
            Assert.Null(f.Confidence);
        });
    }
}

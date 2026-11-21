using RibatIQ.Application.Rules;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Tests;

public sealed class RuleCatalogTests
{
    [Fact]
    public void Default_LoadsTheEmbeddedCatalogue()
    {
        var catalog = RuleCatalog.Default;

        Assert.Equal(41, catalog.Rules.Count);
        Assert.Equal(["default", "security", "performance"], catalog.Profiles.Select(p => p.Name));
        Assert.Same(catalog, RuleCatalog.Default);
    }

    [Fact]
    public void Default_RulesAreUnique_AndClassified()
    {
        var catalog = RuleCatalog.Default;

        Assert.Equal(catalog.Rules.Count, catalog.Rules.Select(r => r.Id).Distinct().Count());
        Assert.All(catalog.Rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.False(string.IsNullOrWhiteSpace(r.Description));
            Assert.True(Enum.IsDefined(r.Category));
            Assert.True(Enum.IsDefined(r.DefaultSeverity));
        });
    }

    [Fact]
    public void Default_OnlyDeclaresDetectionMethodsThatExist()
    {
        var detections = RuleCatalog.Default.Rules.Select(r => r.Detection).Distinct().Order();

        // Test, coverage and configuration analysis have no detector yet: no rule may claim them.
        Assert.Equal([DetectionMethod.StaticAnalysis, DetectionMethod.DependencyAudit, DetectionMethod.AiReview], detections);
    }

    [Fact]
    public void Default_AiRulesNeverDefaultToCritical_NorCoverDependencies()
    {
        var aiRules = RuleCatalog.Default.RulesDetectedBy(DetectionMethod.AiReview);

        Assert.NotEmpty(aiRules);
        Assert.All(aiRules, r =>
        {
            Assert.NotEqual(FindingSeverity.Critical, r.DefaultSeverity);
            Assert.NotEqual(FindingCategory.Dependencies, r.Category);
        });
    }

    [Fact]
    public void Default_ContainsTheDocumentedRules()
    {
        var catalog = RuleCatalog.Default;

        var secret = catalog.Get(ReviewRuleId.Parse("SECURITY.EXPOSED_SECRET"));
        Assert.Equal(FindingCategory.Security, secret.Category);
        Assert.Equal(FindingSeverity.High, secret.DefaultSeverity);
        Assert.Equal(DetectionMethod.StaticAnalysis, secret.Detection);
        Assert.True(secret.EnabledByDefault);

        var naming = catalog.Get(ReviewRuleId.Parse("CLEANCODE.NAMING"));
        Assert.False(naming.EnabledByDefault);
        Assert.Equal(DetectionMethod.AiReview, naming.Detection);

        var vulnerable = catalog.Get(ReviewRuleId.Parse("DEPENDENCY.NUGET.VULNERABLE"));
        Assert.Equal(DetectionMethod.DependencyAudit, vulnerable.Detection);
        Assert.Equal(FindingCategory.Dependencies, vulnerable.Category);
        Assert.Throws<KeyNotFoundException>(() => catalog.Get(ReviewRuleId.Parse("NOT.A_RULE")));
    }

    [Fact]
    public void Default_ProfilesReferenceOnlyCatalogueRules()
    {
        var catalog = RuleCatalog.Default;

        Assert.All(catalog.Profiles, p => Assert.All(p.Enable.Concat(p.Disable), id => Assert.NotNull(catalog.Find(id))));
        Assert.Equal([FindingCategory.Security], catalog.FindProfile("security")!.Categories!);
        Assert.Null(catalog.FindProfile("default")!.Categories);
        Assert.Null(catalog.FindProfile("strict"));
    }

    [Fact]
    public void Parse_ValidMinimalCatalogue()
    {
        var catalog = RuleCatalog.Parse(Catalog(Rule("A.ONE"), Rule("A.TWO", enabled: false)));

        Assert.Equal(["A.ONE", "A.TWO"], catalog.Rules.Select(r => r.Id.Value));
        Assert.False(catalog.Get(ReviewRuleId.Parse("A.TWO")).EnabledByDefault);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": 1, "rules": [], "profiles": [], "extra": true }""")]
    public void Parse_MalformedDocument_IsRejected(string json) =>
        Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(json));

    [Fact]
    public void Parse_UnknownFieldInARule_IsRejected()
    {
        var json = Catalog(Rule("A.ONE").Replace("\"enabledByDefault\"", "\"severityOverride\": \"low\", \"enabledByDefault\"", StringComparison.Ordinal));

        Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(json));
    }

    [Theory]
    [InlineData("""{ "version": 2, "rules": [RULE], "profiles": [DEFAULT] }""", "version must be 1")]
    [InlineData("""{ "version": 1, "rules": [], "profiles": [DEFAULT] }""", "defines no rules")]
    [InlineData("""{ "version": 1, "rules": [RULE], "profiles": [] }""", "'default' profile is required")]
    public void Parse_StructuralProblems_AreReported(string template, string expected)
    {
        var json = template.Replace("RULE", Rule("A.ONE"), StringComparison.Ordinal).Replace("DEFAULT", DefaultProfile, StringComparison.Ordinal);

        var ex = Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(json));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id", "a.one", "not a valid rule ID")]
    [InlineData("category", "reliability", "unknown category")]
    [InlineData("category", "Security", "unknown category")]
    [InlineData("defaultSeverity", "warning", "unknown default severity")]
    [InlineData("detection", "static", "unknown detection")]
    [InlineData("title", "", "title is required")]
    [InlineData("description", "  ", "description is required")]
    public void Parse_InvalidRuleFields_AreReported(string field, string value, string expected)
    {
        var rule = SetField(Rule("A.ONE"), field, value);

        var ex = Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(Catalog(rule)));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateRule_IsReported() =>
        Assert.Contains("defined more than once", Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(Catalog(Rule("A.ONE"), Rule("A.ONE")))).Message, StringComparison.Ordinal);

    [Fact]
    public void Parse_MissingEnabledByDefault_IsReported()
    {
        var rule = Rule("A.ONE").Replace("\"enabledByDefault\": true,", string.Empty, StringComparison.Ordinal);

        Assert.Contains("enabledByDefault is required", Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(Catalog(rule))).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("critical", "correctness", "cannot default to critical")]
    [InlineData("high", "dependencies", "must be detected by tools")]
    public void Parse_AiRulesThatOverstepTheirRole_AreReported(string severity, string category, string expected)
    {
        var rule = SetField(SetField(SetField(Rule("A.ONE"), "detection", "ai-review"), "defaultSeverity", severity), "category", category);

        Assert.Contains(expected, Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(Catalog(rule))).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "name": "custom", "description": "d", "enable": ["A.MISSING"] }""", "not a rule in the catalogue")]
    [InlineData("""{ "name": "custom", "description": "d", "enable": ["A.ONE"], "disable": ["A.ONE"] }""", "both enabled and disabled")]
    [InlineData("""{ "name": "custom", "description": "d", "categories": [] }""", "categories must not be empty")]
    [InlineData("""{ "name": "custom", "description": "d", "categories": ["style"] }""", "unknown category")]
    [InlineData("""{ "name": "Custom", "description": "d" }""", "name must use lower-case")]
    [InlineData("""{ "name": "default", "description": "again" }""", "defined more than once")]
    public void Parse_InvalidProfiles_AreReported(string profile, string expected)
    {
        var json = $$"""{ "version": 1, "rules": [{{Rule("A.ONE")}}], "profiles": [{{DefaultProfile}}, {{profile}}] }""";

        Assert.Contains(expected, Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(json)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReportsEveryProblemAtOnce()
    {
        var json = Catalog(SetField(Rule("A.ONE"), "category", "nope"), SetField(Rule("A.TWO"), "detection", "magic"));

        var ex = Assert.Throws<RuleCatalogException>(() => RuleCatalog.Parse(json));

        Assert.Contains("rule A.ONE: unknown category", ex.Message, StringComparison.Ordinal);
        Assert.Contains("rule A.TWO: unknown detection", ex.Message, StringComparison.Ordinal);
    }

    internal const string DefaultProfile = """{ "name": "default", "description": "Defaults." }""";

    internal static string Catalog(params string[] rules) =>
        $$"""{ "version": 1, "rules": [{{string.Join(", ", rules)}}], "profiles": [{{DefaultProfile}}] }""";

    internal static string Rule(string id, bool enabled = true, string category = "correctness", string detection = "static-analysis") =>
        $$"""
        {
          "id": "{{id}}",
          "title": "Title of {{id}}",
          "category": "{{category}}",
          "defaultSeverity": "medium",
          "enabledByDefault": {{(enabled ? "true" : "false")}},
          "detection": "{{detection}}",
          "description": "Description of {{id}}."
        }
        """;

    private static string SetField(string rule, string field, string value) =>
        System.Text.RegularExpressions.Regex.Replace(rule, $"\"{field}\": \"[^\"]*\"", $"\"{field}\": \"{value}\"");
}

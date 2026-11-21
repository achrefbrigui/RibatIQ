using RibatIQ.Application.Configuration;
using RibatIQ.Application.Rules;
using RibatIQ.Domain.Findings;
using RibatIQ.Domain.Policies;
using RibatIQ.Domain.Rules;

namespace RibatIQ.Application.Tests;

public sealed class ReviewPolicyResolverTests
{
    private static readonly RuleCatalog Catalog = RuleCatalog.Default;

    [Fact]
    public void DefaultProfile_EnablesExactlyTheRulesEnabledByDefault()
    {
        var policy = ReviewPolicyResolver.Resolve(Catalog);

        Assert.Equal("default", policy.Profile);
        Assert.Equal(
            Catalog.Rules.Where(r => r.EnabledByDefault).Select(r => r.Id).ToHashSet(),
            policy.EnabledRules.ToHashSet());
        Assert.Empty(policy.DisabledRules);
        Assert.Null(policy.Categories);
        Assert.Equal(FindingSeverity.Info, policy.MinimumSeverity);
        Assert.True(policy.IncludeUnclassified);
    }

    [Theory]
    [InlineData("security", FindingCategory.Security)]
    [InlineData("performance", FindingCategory.Performance)]
    public void CategoryProfiles_EnableOnlyTheirCategory_AndFilterFindings(string profile, FindingCategory category)
    {
        var policy = ReviewPolicyResolver.Resolve(Catalog, profile);

        Assert.Equal(profile, policy.Profile);
        Assert.NotEmpty(policy.EnabledRules);
        Assert.All(policy.EnabledRules, id => Assert.Equal(category, Catalog.Get(id).Category));
        Assert.Equal(
            Catalog.Rules.Where(r => r.EnabledByDefault && r.Category == category).Select(r => r.Id).ToHashSet(),
            policy.EnabledRules.ToHashSet());
        Assert.Equal([category], policy.Categories!);
    }

    [Fact]
    public void SecurityProfile_IncludesTheStaticSecretRule() =>
        Assert.True(ReviewPolicyResolver.Resolve(Catalog, "security").IsEnabled(ReviewRuleId.Parse("SECURITY.EXPOSED_SECRET")));

    [Fact]
    public void ExplicitEnable_TurnsOnARuleDisabledByDefault()
    {
        var policy = ReviewPolicyResolver.Resolve(Catalog, enable: ["CLEANCODE.NAMING"]);

        Assert.True(policy.IsEnabled(ReviewRuleId.Parse("CLEANCODE.NAMING")));
    }

    [Fact]
    public void ExplicitDisable_TurnsOffARule_AndIsRecordedForTraceability()
    {
        var asyncVoid = ReviewRuleId.Parse("DOTNET.ASYNC.ASYNC_VOID");

        var policy = ReviewPolicyResolver.Resolve(Catalog, disable: [" DOTNET.ASYNC.ASYNC_VOID "]);

        Assert.False(policy.IsEnabled(asyncVoid));
        Assert.Contains(asyncVoid, policy.DisabledRules);
    }

    [Fact]
    public void ExplicitEnable_OutsideTheProfileCategories_KeepsThatCategoryReportable()
    {
        var policy = ReviewPolicyResolver.Resolve(Catalog, "security", enable: ["DOTNET.ASYNC.ASYNC_VOID"]);

        Assert.True(policy.IsEnabled(ReviewRuleId.Parse("DOTNET.ASYNC.ASYNC_VOID")));
        Assert.Equal(new HashSet<FindingCategory> { FindingCategory.Security, FindingCategory.BestPractices }, policy.Categories!.ToHashSet());
    }

    [Fact]
    public void ProfileEnableDisableAndCategories_AreAppliedInOrder()
    {
        var catalog = RuleCatalog.Parse($$"""
            {
              "version": 1,
              "rules": [
                {{RuleCatalogTests.Rule("A.DEFAULT_ON")}},
                {{RuleCatalogTests.Rule("A.DEFAULT_OFF", enabled: false)}},
                {{RuleCatalogTests.Rule("A.REMOVED")}},
                {{RuleCatalogTests.Rule("B.OTHER_CATEGORY", category: "performance")}}
              ],
              "profiles": [
                {{RuleCatalogTests.DefaultProfile}},
                { "name": "custom", "description": "d", "categories": ["correctness"], "enable": ["A.DEFAULT_OFF"], "disable": ["A.REMOVED"] }
              ]
            }
            """);

        var policy = ReviewPolicyResolver.Resolve(catalog, "custom");

        Assert.Equal(["A.DEFAULT_OFF", "A.DEFAULT_ON"], policy.EnabledRules.Select(r => r.Value).Order(StringComparer.Ordinal));
        Assert.Equal(["A.REMOVED"], policy.DisabledRules.Select(r => r.Value));

        var reenabled = ReviewPolicyResolver.Resolve(catalog, "custom", enable: ["A.REMOVED"]);
        Assert.True(reenabled.IsEnabled(ReviewRuleId.Parse("A.REMOVED")));
        Assert.Empty(reenabled.DisabledRules);
    }

    [Fact]
    public void UnknownProfile_ListsTheAvailableOnes()
    {
        var ex = Assert.Throws<ConfigurationException>(() => ReviewPolicyResolver.Resolve(Catalog, "strict"));

        Assert.Contains("'strict'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("default, security, performance", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SECURITY.NOT_A_RULE")]
    [InlineData("security.sql_injection")]
    [InlineData("RIQ1001")]
    public void UnknownOrMalformedRule_IsAConfigurationError(string rule)
    {
        Assert.Contains(rule, Assert.Throws<ConfigurationException>(() => ReviewPolicyResolver.Resolve(Catalog, enable: [rule])).Message, StringComparison.Ordinal);
        Assert.Contains(rule, Assert.Throws<ConfigurationException>(() => ReviewPolicyResolver.Resolve(Catalog, disable: [rule])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameRuleEnabledAndDisabled_IsAConfigurationError() =>
        Assert.Throws<ConfigurationException>(() =>
            ReviewPolicyResolver.Resolve(Catalog, enable: ["SECURITY.SQL_INJECTION"], disable: ["SECURITY.SQL_INJECTION"]));

    [Fact]
    public void SeverityUnclassifiedAndInstructions_AreCarriedIntoThePolicy()
    {
        var instructions = ProjectInstructions.Create("Use the repository pattern.", "review-rules.md");

        var policy = ReviewPolicyResolver.Resolve(
            Catalog, minimumSeverity: FindingSeverity.Medium, includeUnclassified: false, instructions: instructions);

        Assert.Equal(FindingSeverity.Medium, policy.MinimumSeverity);
        Assert.False(policy.IncludeUnclassified);
        Assert.Same(instructions, policy.Instructions);
    }
}

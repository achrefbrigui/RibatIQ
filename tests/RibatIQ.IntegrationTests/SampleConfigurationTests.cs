using RibatIQ.Application.Configuration;
using RibatIQ.Application.Rules;
using RibatIQ.Cli.Configuration;
using RibatIQ.Domain.Findings;

namespace RibatIQ.IntegrationTests;

/// <summary>Keeps <c>samples/ribatiq.json</c> valid and in line with the documentation.</summary>
public sealed class SampleConfigurationTests
{
    [Fact]
    public async Task SampleConfiguration_LoadsAndResolves()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "ribatiq.json");

        var layer = await ConfigFileLoader.LoadAsync(path, AppContext.BaseDirectory, TestContext.Current.CancellationToken);
        var settings = SettingsResolver.Resolve([layer]);

        Assert.True(settings.AiEnabled);
        Assert.Equal(LlmProviders.Anthropic, settings.Provider);
        Assert.Equal(FindingSeverity.High, settings.FailOn.Threshold);
        Assert.Equal(["src/**/*.cs"], settings.Include);
        Assert.Contains("**/Migrations/**", settings.Exclude);
        Assert.Contains("**/bin/**", settings.Exclude);

        Assert.Equal("default", settings.Profile);
        Assert.Equal(FindingSeverity.Low, settings.MinimumSeverity);
        Assert.False(settings.AuditDependencies);

        // The sample only names rules that exist.
        var policy = ReviewPolicyResolver.Resolve(
            RuleCatalog.Default, settings.Profile, settings.EnableRules, settings.DisableRules, settings.MinimumSeverity);
        Assert.Contains(policy.EnabledRules, r => r.Value == "CLEANCODE.NAMING");
    }
}

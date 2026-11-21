using RibatIQ.Application.Configuration;
using RibatIQ.Cli.Configuration;
using RibatIQ.Domain.Findings;

namespace RibatIQ.IntegrationTests;

/// <summary>The <c>review</c> section of <c>ribatiq.json</c>.</summary>
public sealed class ReviewConfigurationTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReviewSection_IsLoaded()
    {
        _directory.WriteFile("config/ribatiq.json", """
            {
              "version": 1,
              "review": {
                "profile": "security",
                "enableRules": ["CLEANCODE.NAMING"],
                "disableRules": ["SECURITY.EXPOSED_SECRET"],
                "minimumSeverity": "Medium",
                "instructions": "../docs/rules.md",
                "auditDependencies": true
              }
            }
            """);

        var layer = await ConfigFileLoader.LoadAsync("config/ribatiq.json", _directory.Path, Ct);

        Assert.Equal("security", layer.Profile);
        Assert.Equal(["CLEANCODE.NAMING"], layer.EnableRules);
        Assert.Equal(["SECURITY.EXPOSED_SECRET"], layer.DisableRules);
        Assert.Equal(FindingSeverity.Medium, layer.MinimumSeverity);
        Assert.Equal(Path.Combine(_directory.Path, "docs", "rules.md"), layer.InstructionsPath);
        Assert.True(layer.AuditDependencies);
    }

    [Fact]
    public async Task WithoutReviewSection_NothingIsSet()
    {
        _directory.WriteFile("ribatiq.json", """{ "version": 1 }""");

        var layer = await ConfigFileLoader.LoadAsync(null, _directory.Path, Ct);

        Assert.Null(layer.Profile);
        Assert.Null(layer.EnableRules);
        Assert.Null(layer.MinimumSeverity);
        Assert.Null(layer.InstructionsPath);
        Assert.Null(layer.AuditDependencies);
    }

    [Theory]
    [InlineData("""{ "version": 1, "review": { "minimumSeverity": "none" } }""", "review.minimumSeverity")]
    [InlineData("""{ "version": 1, "review": { "instructions": " " } }""", "review.instructions")]
    [InlineData("""{ "version": 1, "review": { "rules": [] } }""", "review.rules")]
    [InlineData("""{ "version": 1, "review": { "auditDependencies": "yes" } }""", "review.auditDependencies")]
    public async Task InvalidReviewSettings_AreConfigurationErrors(string json, string setting)
    {
        _directory.WriteFile("ribatiq.json", json);

        var ex = await Assert.ThrowsAsync<ConfigurationException>(() => ConfigFileLoader.LoadAsync(null, _directory.Path, Ct));

        Assert.Contains(setting, ex.Message, StringComparison.Ordinal);
    }
}

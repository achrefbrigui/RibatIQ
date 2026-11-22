using RibatIQ.Application.Configuration;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Tests;

/// <summary>How the review policy settings are merged across the command line, environment and file.</summary>
public sealed class ReviewSettingsResolverTests
{
    private static readonly SettingsLayer Cli = new("command line");
    private static readonly SettingsLayer File = new("configuration file");

    private static readonly string AbsolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rules.md"));

    [Fact]
    public void Resolve_WithoutReviewSettings_UsesTheDefaultPolicy()
    {
        var settings = SettingsResolver.Resolve([Cli, File]);

        Assert.Null(settings.Profile);
        Assert.Empty(settings.EnableRules);
        Assert.Empty(settings.DisableRules);
        Assert.Equal(FindingSeverity.Info, settings.MinimumSeverity);
        Assert.Null(settings.InstructionsPath);
        Assert.False(settings.AuditDependencies);
    }

    [Fact]
    public void Resolve_SingleValues_ComeFromTheHighestLayerThatSetsThem()
    {
        var settings = SettingsResolver.Resolve([
            Cli with { Profile = " security ", MinimumSeverity = FindingSeverity.High },
            File with { Profile = "performance", MinimumSeverity = FindingSeverity.Low, InstructionsPath = AbsolutePath, AuditDependencies = true },
        ]);

        Assert.Equal("security", settings.Profile);
        Assert.Equal(FindingSeverity.High, settings.MinimumSeverity);
        Assert.Equal(AbsolutePath, settings.InstructionsPath);
        Assert.True(settings.AuditDependencies);
    }

    [Fact]
    public void Resolve_CommandLineCanTurnOffTheFilesDependencyAudit()
    {
        var settings = SettingsResolver.Resolve([Cli with { AuditDependencies = false }, File with { AuditDependencies = true }]);

        Assert.False(settings.AuditDependencies);
    }

    [Fact]
    public void Resolve_RuleLists_AreMerged_AndAHigherLayerWins()
    {
        var settings = SettingsResolver.Resolve([
            Cli with { EnableRules = ["CLEANCODE.NAMING"], DisableRules = ["SECURITY.EXPOSED_SECRET"] },
            File with { EnableRules = ["SECURITY.EXPOSED_SECRET", "ARCHITECTURE.COUPLING"], DisableRules = [" CLEANCODE.NAMING "] },
        ]);

        Assert.Equal(["ARCHITECTURE.COUPLING", "CLEANCODE.NAMING"], settings.EnableRules);
        Assert.Equal(["SECURITY.EXPOSED_SECRET"], settings.DisableRules);
    }

    [Fact]
    public void Resolve_RuleEnabledAndDisabledInTheSameLayer_Throws()
    {
        var ex = Assert.Throws<ConfigurationException>(() => SettingsResolver.Resolve([
            Cli with { EnableRules = ["CLEANCODE.NAMING"], DisableRules = ["CLEANCODE.NAMING"] },
        ]));

        Assert.Contains("CLEANCODE.NAMING", ex.Message, StringComparison.Ordinal);
        Assert.Contains("command line", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("RULE\nID")]
    public void Resolve_MalformedRule_Throws(string rule) =>
        Assert.Throws<ConfigurationException>(() => SettingsResolver.Resolve([File with { DisableRules = [rule] }]));

    [Fact]
    public void Resolve_RelativeInstructionsPath_Throws() =>
        Assert.Throws<ConfigurationException>(() => SettingsResolver.Resolve([Cli with { InstructionsPath = "rules.md" }]));

    [Fact]
    public void Resolve_UndefinedSeverity_Throws() =>
        Assert.Throws<ConfigurationException>(() => SettingsResolver.Resolve([Cli with { MinimumSeverity = (FindingSeverity)42 }]));

    [Theory]
    [InlineData("info", FindingSeverity.Info)]
    [InlineData(" HIGH ", FindingSeverity.High)]
    [InlineData("Critical", FindingSeverity.Critical)]
    public void SeverityNames_Parse(string value, FindingSeverity expected)
    {
        Assert.True(SeverityNames.TryParse(value, out var severity));
        Assert.Equal(expected, severity);
        Assert.Equal(value.Trim().ToLowerInvariant(), SeverityNames.Of(severity));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    [InlineData("severe")]
    public void SeverityNames_RejectUnknownNames(string? value) => Assert.False(SeverityNames.TryParse(value, out _));
}

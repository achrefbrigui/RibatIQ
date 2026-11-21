using RibatIQ.Application.Configuration;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Tests;

public sealed class SettingsResolverTests
{
    private static readonly SettingsLayer EmptyCli = new("command line");
    private static readonly SettingsLayer EmptyEnv = new("environment");
    private static readonly SettingsLayer EmptyFile = new("configuration file");

    [Fact]
    public void Resolve_WithNoSettings_UsesSafeDefaults()
    {
        var settings = SettingsResolver.Resolve([EmptyCli, EmptyEnv, EmptyFile]);

        Assert.False(settings.AiEnabled);
        Assert.Null(settings.Provider);
        Assert.Null(settings.Model);
        Assert.Same(FailPolicy.Never, settings.FailOn);
        Assert.Empty(settings.Include);
        Assert.Equal(SettingsResolver.DefaultExcludes, settings.Exclude);
    }

    [Fact]
    public void Resolve_WithProvider_EnablesAi_AndNormalizesProvider()
    {
        var settings = SettingsResolver.Resolve([EmptyCli, EmptyEnv, EmptyFile with { Provider = " OpenAI ", Model = " m1 " }]);

        Assert.True(settings.AiEnabled);
        Assert.Equal(LlmProviders.OpenAi, settings.Provider);
        Assert.Equal("m1", settings.Model);
    }

    [Fact]
    public void Resolve_CommandLineOverridesEnvironment_WhichOverridesFile()
    {
        var cli = EmptyCli with { Provider = "anthropic" };
        var env = EmptyEnv with { Provider = "openai", Model = "env-model", FailOn = FailPolicy.AtOrAbove(FindingSeverity.Critical) };
        var file = EmptyFile with { Model = "file-model", FailOn = FailPolicy.AtOrAbove(FindingSeverity.Low) };

        var settings = SettingsResolver.Resolve([cli, env, file]);

        Assert.Equal(LlmProviders.Anthropic, settings.Provider);
        Assert.Equal("env-model", settings.Model);
        Assert.Equal(FindingSeverity.Critical, settings.FailOn.Threshold);
    }

    [Fact]
    public void Resolve_WithAiExplicitlyDisabled_DropsProviderAndModel()
    {
        var settings = SettingsResolver.Resolve(
            [EmptyCli with { AiEnabled = false }, EmptyEnv with { Provider = "openai", Model = "m" }, EmptyFile]);

        Assert.False(settings.AiEnabled);
        Assert.Null(settings.Provider);
        Assert.Null(settings.Model);
    }

    [Fact]
    public void Resolve_WithAiEnabledButNoProvider_ThrowsNamingTheSource()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            SettingsResolver.Resolve([EmptyCli, EmptyEnv, EmptyFile with { AiEnabled = true }]));

        Assert.Contains("configuration file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WithUnknownProvider_ThrowsNamingTheSource()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            SettingsResolver.Resolve([EmptyCli, EmptyEnv with { Provider = "gemini" }, EmptyFile]));

        Assert.Contains("gemini", ex.Message, StringComparison.Ordinal);
        Assert.Contains("environment", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_Include_ComesFromHighestLayerWithValues()
    {
        var settings = SettingsResolver.Resolve(
            [EmptyCli with { Include = [] }, EmptyEnv, EmptyFile with { Include = ["src/**/*.cs", "src/**/*.cs"] }]);

        Assert.Equal(["src/**/*.cs"], settings.Include);

        var overridden = SettingsResolver.Resolve(
            [EmptyCli with { Include = ["tests/**"] }, EmptyEnv, EmptyFile with { Include = ["src/**"] }]);

        Assert.Equal(["tests/**"], overridden.Include);
    }

    [Fact]
    public void Resolve_Exclude_IsAdditiveAndAlwaysKeepsDefaults()
    {
        var settings = SettingsResolver.Resolve(
            [EmptyCli with { Exclude = ["cli/**", "**/bin/**"] }, EmptyEnv, EmptyFile with { Exclude = ["file/**"] }]);

        Assert.Equal(["**/bin/**", "**/obj/**", "**/.git/**", "file/**", "cli/**"], settings.Exclude);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("src/\n**")]
    public void Resolve_WithInvalidGlob_Throws(string glob)
    {
        Assert.Throws<ConfigurationException>(() => SettingsResolver.Resolve([EmptyCli with { Exclude = [glob] }]));
        Assert.Throws<ConfigurationException>(() => SettingsResolver.Resolve([EmptyCli with { Include = [glob] }]));
    }

    [Fact]
    public void Resolve_WithOverlongGlob_Throws() =>
        Assert.Throws<ConfigurationException>(() =>
            SettingsResolver.Resolve([EmptyCli with { Include = [new string('*', SettingsResolver.MaxGlobLength + 1)] }]));
}

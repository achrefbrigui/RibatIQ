using RibatIQ.Cli;

namespace RibatIQ.IntegrationTests;

/// <summary>The review policy options of <c>scan</c> and <c>review</c>, run in-process without AI.</summary>
public sealed class ReviewOptionsCliTests : IDisposable
{
    private const string EmptyCatch = "public class C { public void M() { try { M(); } catch { } } }";

    private readonly TempDirectory _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Help_ListsThePolicyOptions_AndTheAuditOnlyForScan()
    {
        var scanHelp = (await RunAsync("scan", "--help")).Output;
        foreach (var option in new[] { "--profile", "--enable-rule", "--disable-rule", "--min-severity", "--instructions", "--audit-dependencies" })
        {
            Assert.Contains(option, scanHelp, StringComparison.Ordinal);
        }

        var reviewHelp = (await RunAsync("review", "--help")).Output;
        Assert.Contains("--profile", reviewHelp, StringComparison.Ordinal);
        Assert.DoesNotContain("--audit-dependencies", reviewHelp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--profile", "paranoid")]
    [InlineData("--min-severity", "none")]
    [InlineData("--min-severity", "severe")]
    public async Task InvalidOptionValues_AreInvalidUsage(string option, string value) =>
        Assert.Equal(ExitCodes.InvalidUsage, (await RunAsync("scan", ".", "--no-ai", option, value)).ExitCode);

    [Fact]
    public async Task ReviewWithAuditDependencies_IsInvalidUsage() =>
        Assert.Equal(ExitCodes.InvalidUsage, (await RunAsync("review", "--base", "main", "--audit-dependencies")).ExitCode);

    [Fact]
    public async Task UnknownRule_IsAConfigurationError()
    {
        var result = await RunAsync("scan", ".", "--no-ai", "--enable-rule", "SECURITY.TELEPATHY");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("SECURITY.TELEPATHY", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameRuleEnabledAndDisabled_IsAConfigurationError()
    {
        var result = await RunAsync("scan", ".", "--no-ai", "--enable-rule", "CLEANCODE.NAMING", "--disable-rule", "cleancode.naming");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("both enabled and disabled", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verbose_ShowsTheEffectivePolicySettings()
    {
        var result = await RunAsync(
            "scan", ".", "--no-ai", "--verbose", "--profile", "security", "--enable-rule", "cleancode.naming", "--min-severity", "medium", "--audit-dependencies");

        Assert.Contains("Profile: security", result.Error, StringComparison.Ordinal);
        Assert.Contains("Enabled rules: CLEANCODE.NAMING", result.Error, StringComparison.Ordinal);
        Assert.Contains("Minimum severity: medium", result.Error, StringComparison.Ordinal);
        Assert.Contains("Dependency audit: requested", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandLine_OverridesTheConfigurationFilesRuleLists()
    {
        _workspace.WriteFile("ribatiq.json", """{ "version": 1, "review": { "disableRules": ["DOTNET.EXCEPTIONS.EMPTY_CATCH"] } }""");
        _workspace.WriteFile("src/C.cs", EmptyCatch);

        var fromFile = await RunAsync("scan", ".", "--no-ai");
        var withCli = await RunAsync("scan", ".", "--no-ai", "--enable-rule", "DOTNET.EXCEPTIONS.EMPTY_CATCH");

        Assert.DoesNotContain("DOTNET.EXCEPTIONS.EMPTY_CATCH", fromFile.Output, StringComparison.Ordinal);
        Assert.Contains("DOTNET.EXCEPTIONS.EMPTY_CATCH (RIQ1001)", withCli.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MinSeverity_HidesLowerFindings_AndSaysSo()
    {
        _workspace.WriteFile("src/C.cs", EmptyCatch);

        var all = await RunAsync("scan", ".", "--no-ai");
        var high = await RunAsync("scan", ".", "--no-ai", "--min-severity", "high");

        Assert.Contains("MEDIUM", all.Output, StringComparison.Ordinal);
        Assert.Contains("No findings.", high.Output, StringComparison.Ordinal);
        Assert.Contains("below minimum severity: 1", high.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Instructions_MissingExplicitFile_IsInvalidUsage()
    {
        var result = await RunAsync("scan", ".", "--no-ai", "--instructions", "docs/missing.md");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("missing.md", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Instructions_RelativePath_IsResolvedFromTheCurrentDirectory()
    {
        _workspace.WriteFile("docs/rules.md", "Prefer records for DTOs.");

        var result = await RunAsync("scan", ".", "--no-ai", "--instructions", "docs/rules.md", "--verbose");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains($"Instructions: {Path.Combine(_workspace.Path, "docs", "rules.md")}", result.Error, StringComparison.Ordinal);
    }

    private async Task<CliResult> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CliContext(output, error, _workspace.Path, _ => null);

        var exitCode = await RibatCli.InvokeAsync(args, context, TestContext.Current.CancellationToken);

        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);
}

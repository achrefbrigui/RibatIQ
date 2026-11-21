using RibatIQ.Cli;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// Runs the CLI in-process with redirected output, a temporary working directory and a fake environment.
/// </summary>
public sealed class CliCommandTests : IDisposable
{
    private readonly TempDirectory _workspace = new();
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void ExitCodes_AreStableContract()
    {
        Assert.Equal(0, ExitCodes.Success);
        Assert.Equal(1, ExitCodes.FindingsAtOrAboveThreshold);
        Assert.Equal(2, ExitCodes.InvalidUsage);
        Assert.Equal(3, ExitCodes.OperationalFailure);
    }

    [Fact]
    public async Task Help_DoesNotAdvertiseCommandsThatAreNotWiredYet()
    {
        var result = await RunAsync("--help");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.DoesNotContain("scan", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("review", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("src/App/App.csproj")]
    public async Task Scan_WithSupportedTarget_ValidatesThenReportsEngineUnavailable(string target)
    {
        _workspace.WriteFile("App.sln", string.Empty);
        _workspace.WriteFile("App.slnx", "<Solution />");
        _workspace.WriteFile("src/App/App.csproj", "<Project />");

        var result = await RunAsync("scan", target);

        Assert.Equal(ExitCodes.OperationalFailure, result.ExitCode);
        Assert.Contains("not available", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_WithPathContainingSpaces_IsAccepted()
    {
        _workspace.WriteFile("My Folder/My App.sln", string.Empty);

        var result = await RunAsync("scan", "My Folder/My App.sln");

        Assert.Equal(ExitCodes.OperationalFailure, result.ExitCode);
        Assert.Contains("My App.sln", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_WithMissingPath_IsInvalidUsage()
    {
        var result = await RunAsync("scan", "missing.sln");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_WithUnsupportedFile_IsInvalidUsage()
    {
        _workspace.WriteFile("notes.txt", "hello");

        var result = await RunAsync("scan", "notes.txt");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("Unsupported file", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_WithoutPath_IsInvalidUsage() =>
        Assert.Equal(ExitCodes.InvalidUsage, (await RunAsync("scan")).ExitCode);

    [Fact]
    public async Task Review_WithValidRevisions_ReportsEngineUnavailableForRange()
    {
        var result = await RunAsync("review", "--base", "origin/main");

        Assert.Equal(ExitCodes.OperationalFailure, result.ExitCode);
        Assert.Contains("origin/main...HEAD", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_WithoutBase_IsInvalidUsage() =>
        Assert.Equal(ExitCodes.InvalidUsage, (await RunAsync("review", "--head", "HEAD")).ExitCode);

    [Theory]
    [InlineData("--base", "--upload-pack=evil")]
    [InlineData("--head", "-p")]
    [InlineData("--head", "two words")]
    public async Task Review_WithRevisionThatCouldBeAGitOption_IsInvalidUsage(string option, string value)
    {
        var args = option == "--base"
            ? new[] { "review", "--base", value }
            : new[] { "review", "--base", "main", option, value };

        var result = await RunAsync(args);

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains(option, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--provider", "gemini")]
    [InlineData("--format", "xml")]
    [InlineData("--fail-on", "severe")]
    public async Task Scan_WithUnsupportedOptionValue_IsInvalidUsage(string option, string value) =>
        Assert.Equal(ExitCodes.InvalidUsage, (await RunAsync("scan", ".", option, value)).ExitCode);

    [Theory]
    [InlineData("--provider", "openai")]
    [InlineData("--model", "some-model")]
    public async Task NoAi_CannotBeCombinedWithProviderOrModel(string option, string value)
    {
        var result = await RunAsync("scan", ".", "--no-ai", option, value);

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("--no-ai", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verbose_ByDefault_ShowsAiDisabledAndDefaultExclusions()
    {
        var result = await RunAsync("scan", ".", "--verbose");

        Assert.Contains("AI analysis: disabled", result.Error, StringComparison.Ordinal);
        Assert.Contains("Fail on: none", result.Error, StringComparison.Ordinal);
        Assert.Contains("**/obj/**", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task Settings_FollowPrecedence_CommandLineThenEnvironmentThenFile()
    {
        _workspace.WriteFile("ribatiq.json", """
            {
              // comments and trailing commas are allowed
              "version": 1,
              "analysis": { "failOn": "low", "exclude": ["gen/**"], },
              "ai": { "provider": "anthropic", "model": "file-model" },
            }
            """);
        _environment["RIBATIQ_MODEL"] = "env-model";
        _environment["RIBATIQ_FAIL_ON"] = "high";

        var fromFileAndEnv = await RunAsync("review", "--base", "main", "--verbose");

        Assert.Contains("Provider: anthropic", fromFileAndEnv.Error, StringComparison.Ordinal);
        Assert.Contains("Model: env-model", fromFileAndEnv.Error, StringComparison.Ordinal);
        Assert.Contains("Fail on: high", fromFileAndEnv.Error, StringComparison.Ordinal);
        Assert.Contains("gen/**", fromFileAndEnv.Error, StringComparison.Ordinal);

        var withCli = await RunAsync("review", "--base", "main", "--verbose", "--provider", "openai", "--fail-on", "critical");

        Assert.Contains("Provider: openai", withCli.Error, StringComparison.Ordinal);
        Assert.Contains("Fail on: critical", withCli.Error, StringComparison.Ordinal);

        var noAi = await RunAsync("review", "--base", "main", "--verbose", "--no-ai");

        Assert.Contains("AI analysis: disabled", noAi.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Provider:", noAi.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigFile_WithUnknownKey_IsRejectedWithoutEchoingItsValue()
    {
        _workspace.WriteFile("ribatiq.json", """{ "version": 1, "ai": { "provider": "openai", "apiKey": "sk-do-not-print" } }""");

        var result = await RunAsync("scan", ".");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("unknown setting 'ai.apiKey'", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-do-not-print", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-do-not-print", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "version": 2 }""", "\"version\": 1")]
    [InlineData("""{ "analysis": {} }""", "\"version\": 1")]
    [InlineData("""{ "version": 1, "ai": { "enabled": true } }""", "no provider")]
    [InlineData("""{ "version": 1, "ai": { "enabled": "yes" } }""", "ai.enabled")]
    [InlineData("""{ "version": 1, "analysis": { "failOn": "sometimes" } }""", "analysis.failOn")]
    [InlineData("""{ "version": 1, """, "not valid")]
    public async Task ConfigFile_WithInvalidContent_IsInvalidUsage(string json, string expectedMessage)
    {
        _workspace.WriteFile("ribatiq.json", json);

        var result = await RunAsync("scan", ".");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains(expectedMessage, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigOption_WithMissingFile_IsInvalidUsage()
    {
        var result = await RunAsync("scan", ".", "--config", "missing.json");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("missing.json", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigOption_LoadsExplicitFile()
    {
        _workspace.WriteFile("config/custom.json", """{ "version": 1, "ai": { "provider": "openai" } }""");

        var result = await RunAsync("scan", ".", "--config", "config/custom.json", "--verbose");

        Assert.Contains("Provider: openai", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Environment_WithInvalidFailOn_IsInvalidUsage()
    {
        _environment["RIBATIQ_FAIL_ON"] = "sometimes";

        var result = await RunAsync("scan", ".");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("RIBATIQ_FAIL_ON", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Environment_WithUnknownProvider_IsInvalidUsage()
    {
        _environment["RIBATIQ_PROVIDER"] = "gemini";

        var result = await RunAsync("scan", ".");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("environment", result.Error, StringComparison.Ordinal);
    }

    private async Task<CliResult> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CliContext(output, error, _workspace.Path, name => _environment.GetValueOrDefault(name));

        var exitCode = await RibatCli.InvokeAsync(args, context, TestContext.Current.CancellationToken);

        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);
}

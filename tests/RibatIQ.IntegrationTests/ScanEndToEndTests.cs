using RibatIQ.Cli;

namespace RibatIQ.IntegrationTests;

/// <summary>Runs <c>ribat scan</c> in-process against the SampleSolution fixture.</summary>
public sealed class ScanEndToEndTests : IDisposable
{
    private readonly TempDirectory _fixture = TempDirectory.FromFixture("SampleSolution");

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Scan_Solution_ReportsExpectedFindings()
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--no-ai");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Analyzed 3 C# file(s) in 2 project(s).", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Sample.Web/Startup.cs:5  [SECURITY.EXPOSED_SECRET (RIQ1004)]", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Sample.Web/Startup.cs:7  [DOTNET.ASYNC.ASYNC_VOID (RIQ1002)]", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Sample.Core/Issues.cs:11  [DOTNET.EXCEPTIONS.EMPTY_CATCH (RIQ1001)]", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Sample.Core/Issues.cs:24  [DOTNET.EXCEPTIONS.RETHROW_LOSES_STACK (RIQ1003)]", result.Output, StringComparison.Ordinal);
        Assert.Contains("4 finding(s): 0 critical, 1 high, 2 medium, 1 low, 0 info.", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Missing/Missing.csproj", result.Output, StringComparison.Ordinal);
        Assert.Contains("does not prove", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_Solution_OrdersFindingsBySeverity()
    {
        var output = (await RunAsync("scan", "SampleSolution.sln")).Output;

        var high = output.IndexOf("[SECURITY.EXPOSED_SECRET (RIQ1004)]", StringComparison.Ordinal);
        var medium = output.IndexOf("[DOTNET.EXCEPTIONS.EMPTY_CATCH (RIQ1001)]", StringComparison.Ordinal);
        var low = output.IndexOf("[DOTNET.EXCEPTIONS.RETHROW_LOSES_STACK (RIQ1003)]", StringComparison.Ordinal);

        Assert.True(high < medium && medium < low, output);
    }

    [Fact]
    public async Task Scan_Directory_AlsoAnalyzesNestedProjectsAndLooseFiles()
    {
        var result = await RunAsync("scan", ".");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Analyzed 5 C# file(s) in 3 project(s).", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Sample.Web/Nested/Inner.cs:", result.Output, StringComparison.Ordinal);
        Assert.Contains("[CSHARP.SYNTAX.ERROR (CS1513)]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_WithExclude_SkipsMatchingFiles()
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--exclude", "**/Sample.Web/**");

        Assert.Contains("Analyzed 2 C# file(s) in 2 project(s).", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Startup.cs", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_NeverPrintsTheHardcodedSecretValue()
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--verbose");

        Assert.DoesNotContain("P@ssw0rd", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("P@ssw0rd", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_DoesNotModifyAnyFile()
    {
        var before = _fixture.Snapshot();

        await RunAsync("scan", ".");
        await RunAsync("scan", "SampleSolution.sln");

        Assert.Equal(before, _fixture.Snapshot());
    }

    [Fact]
    public async Task Scan_WithAiRequested_WithoutApiKey_IsInvalidUsage_BeforeAnyAnalysis()
    {
        // The test environment has no variables at all, so no API key is available.
        var result = await RunAsync("scan", "SampleSolution.sln", "--provider", "openai");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("OPENAI_API_KEY is not set", result.Error, StringComparison.Ordinal);
        Assert.Contains("--no-ai", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("finding(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_WithFailOnThreshold_WritesTheFullReport_ThenExitsWithCode1()
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--fail-on", "high");

        Assert.Equal(ExitCodes.FindingsAtOrAboveThreshold, result.ExitCode);
        Assert.Contains("4 finding(s):", result.Output, StringComparison.Ordinal);
        Assert.Contains("Result: FAILED - 1 finding(s) at or above 'high'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_MalformedSolution_IsInvalidUsage()
    {
        _fixture.WriteFile("Broken.slnx", "<Solution>");

        var result = await RunAsync("scan", "Broken.slnx");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("Broken.slnx", result.Error, StringComparison.Ordinal);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CliContext(output, error, _fixture.Path, _ => null);

        var exitCode = await RibatCli.InvokeAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }
}

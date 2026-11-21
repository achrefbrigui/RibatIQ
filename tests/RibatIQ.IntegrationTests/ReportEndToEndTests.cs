using System.Text.Json;

using RibatIQ.Cli;

namespace RibatIQ.IntegrationTests;

/// <summary>Report formats, files and exit codes through the real CLI on the SampleSolution fixture.</summary>
public sealed class ReportEndToEndTests : IDisposable
{
    private readonly TempDirectory _fixture = TempDirectory.FromFixture("SampleSolution");
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Scan_JsonOnStandardOutput_IsOnlyTheReport()
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--format", "json", "--verbose");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("scan", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("SampleSolution.sln", json.RootElement.GetProperty("target").GetString());
        Assert.Equal(4, json.RootElement.GetProperty("findings").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("statistics").GetProperty("projects").GetInt32());
        Assert.Contains("[verbose]", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_Json_NeverContainsSourceCodeOrSecretValues()
    {
        var result = await RunAsync("scan", ".", "--format", "json");

        Assert.DoesNotContain("P@ssw0rd", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Yield", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(_fixture.Path, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_OutputFile_WritesReportAndKeepsStandardOutputEmpty()
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--format", "json", "--output", "ribatiq-report.json");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Report written to ribatiq-report.json (json).", result.Error, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_fixture.Path, "ribatiq-report.json"), TestContext.Current.CancellationToken));
        Assert.Equal(4, json.RootElement.GetProperty("summary").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Scan_OutputOntoASourceFile_IsRefused_AndTheFileIsUntouched()
    {
        var before = _fixture.Snapshot();

        var result = await RunAsync("scan", "SampleSolution.sln", "--output", "src/Sample.Web/Startup.cs");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Equal(before, _fixture.Snapshot());
    }

    [Theory]
    [InlineData("critical", ExitCodes.Success)]
    [InlineData("high", ExitCodes.FindingsAtOrAboveThreshold)]
    [InlineData("low", ExitCodes.FindingsAtOrAboveThreshold)]
    [InlineData("none", ExitCodes.Success)]
    public async Task FailOn_FromCommandLine_SetsTheExitCode(string failOn, int expected)
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--fail-on", failOn);

        Assert.Equal(expected, result.ExitCode);
        Assert.Contains("finding(s):", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailOn_FromEnvironmentAndConfigFile_IsApplied_WithCommandLinePrecedence()
    {
        _fixture.WriteFile("ribatiq.json", """{ "version": 1, "analysis": { "failOn": "low" } }""");
        Assert.Equal(ExitCodes.FindingsAtOrAboveThreshold, (await RunAsync("scan", "SampleSolution.sln")).ExitCode);

        _environment["RIBATIQ_FAIL_ON"] = "critical";
        Assert.Equal(ExitCodes.Success, (await RunAsync("scan", "SampleSolution.sln")).ExitCode);

        Assert.Equal(ExitCodes.FindingsAtOrAboveThreshold, (await RunAsync("scan", "SampleSolution.sln", "--fail-on", "medium")).ExitCode);
    }

    [Fact]
    public async Task FailOn_ReportIsCompleteEvenWhenTheRunFails()
    {
        var result = await RunAsync("scan", "SampleSolution.sln", "--fail-on", "low", "--format", "json");

        Assert.Equal(ExitCodes.FindingsAtOrAboveThreshold, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(4, json.RootElement.GetProperty("findings").GetArrayLength());
        Assert.Equal(4, json.RootElement.GetProperty("gate").GetProperty("failingFindings").GetInt32());
        Assert.True(json.RootElement.GetProperty("gate").GetProperty("thresholdExceeded").GetBoolean());
    }

    [Fact]
    public async Task Review_JsonReport_HasReviewStatistics()
    {
        using var repo = new TempGitRepository();
        repo.WriteFile("src/A.cs", "public class A { }\n");
        repo.Commit("base");
        repo.Git("switch", "-q", "-c", "feature");
        repo.WriteFile("src/A.cs", "public class A { void M() { try { } catch { } } }\n");
        repo.Commit("feature");

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await RibatCli.InvokeAsync(
            ["review", "--base", "main", "--format", "json", "--fail-on", "medium"],
            new CliContext(output, error, repo.Path, _ => null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.FindingsAtOrAboveThreshold, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal("review", root.GetProperty("kind").GetString());
        Assert.Equal("main...HEAD", root.GetProperty("target").GetString());
        Assert.Equal(1, root.GetProperty("statistics").GetProperty("changedFiles").GetInt32());
        Assert.Equal("RIQ1001", root.GetProperty("findings")[0].GetProperty("ruleId").GetString());
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CliContext(output, error, _fixture.Path, name => _environment.GetValueOrDefault(name));

        var exitCode = await RibatCli.InvokeAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }
}

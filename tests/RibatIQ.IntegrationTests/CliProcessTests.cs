using System.Diagnostics;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// End-to-end tests that launch the built <c>ribat</c> executable as a real process.
/// </summary>
public sealed class CliProcessTests
{
    [Fact]
    public async Task Version_PrintsProductVersion_AndExitsZero()
    {
        var result = await RunRibatAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0.1.0", result.StandardOutput.Trim());
        Assert.Empty(result.StandardError);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task Help_DescribesTool_AndExitsZero(string helpOption)
    {
        var result = await RunRibatAsync(helpOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("RibatIQ", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("--version", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownOption_ExitsWithInvalidUsage()
    {
        var result = await RunRibatAsync("--definitely-not-an-option");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--definitely-not-an-option", result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunRibatAsync(params string[] arguments)
    {
        var ribatDll = Path.Combine(AppContext.BaseDirectory, "ribat.dll");
        Assert.True(File.Exists(ribatDll), $"CLI assembly not found at '{ribatDll}'.");

        // Arguments are passed as a list, never concatenated into a shell string.
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ribatDll);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the ribat process.");

        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

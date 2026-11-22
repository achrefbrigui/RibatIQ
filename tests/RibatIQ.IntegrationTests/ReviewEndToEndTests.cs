using RibatIQ.Cli;

namespace RibatIQ.IntegrationTests;

/// <summary>Runs <c>ribat review</c> in-process against temporary Git repositories.</summary>
public sealed class ReviewEndToEndTests : IDisposable
{
    private const string BaseService = """
        public class Service
        {
            public void Existing()
            {
                try { Existing(); } catch { }
            }
        }
        """;

    private readonly TempGitRepository _repo = new();

    public ReviewEndToEndTests()
    {
        _repo.WriteFile("src/Service.cs", BaseService + "\n");
        _repo.Commit("base");
        _repo.Git("switch", "-q", "-c", "feature");
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Review_ReportsOnlyFindingsIntroducedByTheChanges()
    {
        _repo.WriteFile("src/Service.cs", """
            public class Service
            {
                public void Existing()
                {
                    try { Existing(); } catch { }
                }

                public async void Fire() { await System.Threading.Tasks.Task.Yield(); }
            }

            """);
        _repo.WriteFile("src/New Folder/Config.cs", """
            public class Config
            {
                private const string ApiKey = "sk-live-0123456789";
            }

            """);
        _repo.Commit("feature");

        var result = await RunAsync("review", "--base", "main");

        Assert.True(result.ExitCode == ExitCodes.Success, result.Error);
        Assert.Contains("RibatIQ review: main...HEAD", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/Service.cs:8  [DOTNET.ASYNC.ASYNC_VOID (RIQ1002)]", result.Output, StringComparison.Ordinal);
        Assert.Contains("src/New Folder/Config.cs:3  [SECURITY.EXPOSED_SECRET (RIQ1004)]", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("[DOTNET.EXCEPTIONS.EMPTY_CATCH (RIQ1001)]", result.Output, StringComparison.Ordinal);
        Assert.Contains("1 finding(s) outside changed lines ignored", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 finding(s):", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_ReadsHeadRevision_NotUncommittedWorkingTreeChanges()
    {
        _repo.WriteFile("src/Clean.cs", "public class Clean { }\n");
        _repo.Commit("feature");
        _repo.WriteFile("src/Clean.cs", "public class Clean { void M() { try { } catch { } } }\n");

        var result = await RunAsync("review", "--base", "main", "--head", "feature");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("No findings.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_IgnoresDeletedAndNonCSharpFiles()
    {
        _repo.DeleteFile("src/Service.cs");
        _repo.WriteFile("README.md", "docs\n");
        _repo.Commit("feature");

        var result = await RunAsync("review", "--base", "main");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("Reviewed 0 changed C# file(s) of 2 changed file(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("No C# changes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_WithExclude_SkipsMatchingChangedFiles()
    {
        _repo.WriteFile("src/Generated/Api.cs", "public class Api { void M() { try { } catch { } } }\n");
        _repo.Commit("feature");

        var result = await RunAsync("review", "--base", "main", "--exclude", "**/Generated/**");

        Assert.Contains("Reviewed 0 changed C# file(s)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("[DOTNET.EXCEPTIONS.EMPTY_CATCH (RIQ1001)]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_WithNoChanges_SucceedsWithWarning()
    {
        var result = await RunAsync("review", "--base", "main", "--head", "main");

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Contains("No C# changes to review between main and main.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_FromSubdirectory_UsesRepositoryRootPaths()
    {
        _repo.WriteFile("src/Sub/Bad.cs", "public class Bad { void M() { try { } catch { } } }\n");
        _repo.Commit("feature");

        var result = await RunInDirectoryAsync(Path.Combine(_repo.Path, "src", "Sub"), "review", "--base", "main");

        Assert.Contains("src/Sub/Bad.cs:1  [DOTNET.EXCEPTIONS.EMPTY_CATCH (RIQ1001)]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_UnknownRevision_IsInvalidUsage()
    {
        var result = await RunAsync("review", "--base", "origin/does-not-exist");

        Assert.Equal(ExitCodes.InvalidUsage, result.ExitCode);
        Assert.Contains("origin/does-not-exist", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task Review_DoesNotModifyTheRepository()
    {
        _repo.WriteFile("src/Service.cs", BaseService + "\n// change\n");
        _repo.Commit("feature");
        var statusBefore = _repo.Git("status", "--porcelain");
        var headBefore = _repo.Git("rev-parse", "HEAD");

        await RunAsync("review", "--base", "main");

        Assert.Equal(statusBefore, _repo.Git("status", "--porcelain"));
        Assert.Equal(headBefore, _repo.Git("rev-parse", "HEAD"));
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args) =>
        RunInDirectoryAsync(_repo.Path, args);

    private static async Task<(int ExitCode, string Output, string Error)> RunInDirectoryAsync(string workingDirectory, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var context = new CliContext(output, error, workingDirectory, _ => null);

        var exitCode = await RibatCli.InvokeAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }
}

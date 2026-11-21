using System.Diagnostics;

namespace RibatIQ.IntegrationTests;

/// <summary>A throwaway Git repository for tests, with a fixed identity and no global hooks or signing.</summary>
internal sealed class TempGitRepository : IDisposable
{
    private readonly TempDirectory _directory;

    public TempGitRepository(string initialBranch = "main")
    {
        _directory = new TempDirectory();
        Git("init", "-q", "-b", initialBranch);
    }

    private TempGitRepository(TempDirectory directory) => _directory = directory;

    public string Path => _directory.Path;

    /// <summary>Creates a shallow (depth 1) clone of <paramref name="source"/>.</summary>
    public static TempGitRepository ShallowCloneOf(TempGitRepository source)
    {
        var directory = new TempDirectory();
        var url = new Uri(source.Path).AbsoluteUri;
        RunGit(directory.Path, "clone", "-q", "--depth", "1", url, ".");
        return new TempGitRepository(directory);
    }

    public string WriteFile(string relativePath, string content) => _directory.WriteFile(relativePath, content);

    public void DeleteFile(string relativePath) => File.Delete(System.IO.Path.Combine(Path, relativePath));

    public string Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "--allow-empty", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    public string Git(params string[] arguments) => RunGit(Path, arguments);

    public void Dispose() => _directory.Dispose();

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var setting in new[]
        {
            "user.name=RibatIQ Tests", "user.email=tests@ribatiq.invalid", "commit.gpgsign=false",
            "core.autocrlf=false", "core.hooksPath=/dev/null", "init.defaultBranch=main",
        })
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(setting);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }
}

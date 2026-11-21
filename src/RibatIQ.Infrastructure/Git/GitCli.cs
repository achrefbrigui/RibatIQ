using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using RibatIQ.Application;
using RibatIQ.Application.Reviewing;

namespace RibatIQ.Infrastructure.Git;

/// <summary>
/// <see cref="IGitClient"/> backed by the Git command line. Arguments are always passed as a list,
/// never through a shell, and every command is read-only.
/// </summary>
public sealed class GitCli : IGitClient
{
    private const string ShallowHint =
        "The repository is a shallow clone, so the history needed for this revision may be missing. " +
        "Fetch more history (for example 'git fetch --unshallow', or 'fetch-depth: 0' with actions/checkout).";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly string _executable;
    private readonly TimeSpan _timeout;

    public GitCli(string executable = "git", TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        _executable = executable;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<string> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await RunAsync(workingDirectory, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidInputException($"'{workingDirectory}' is not inside a Git repository.");
        }

        return Path.GetFullPath(result.Output.Trim());
    }

    public async Task<string> GetDiffAsync(string repositoryRoot, string baseRef, string headRef, CancellationToken cancellationToken)
    {
        await VerifyCommitAsync(repositoryRoot, baseRef, cancellationToken).ConfigureAwait(false);
        await VerifyCommitAsync(repositoryRoot, headRef, cancellationToken).ConfigureAwait(false);

        var result = await RunAsync(
            repositoryRoot,
            [
                "-c", "core.quotepath=false",
                "diff",
                "--no-color", "--no-ext-diff", "--no-textconv",
                "--unified=0", "--find-renames",
                $"{baseRef}...{headRef}",
                "--",
            ],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            return result.Output;
        }

        if (result.Error.Contains("no merge base", StringComparison.OrdinalIgnoreCase))
        {
            var shallow = await IsShallowAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
            throw new InvalidInputException(
                $"'{baseRef}' and '{headRef}' have no common history." + (shallow ? " " + ShallowHint : string.Empty));
        }

        throw Failure("diff", result);
    }

    public async Task<long?> GetFileSizeAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken)
    {
        // "<mode> <type> <object> <size>\t<path>", or nothing when the path does not exist at that revision.
        var result = await RunAsync(
            repositoryRoot, ["-c", "core.quotepath=false", "ls-tree", "-l", "--end-of-options", revision, "--", path], cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw Failure($"inspect '{path}' at {revision}", result);
        }

        var line = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var fields = line?.Split('\t', 2)[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Only regular files count: symbolic links (120000), directories and submodules are treated as absent.
        if (fields is not { Length: 4 } || fields[1] != "blob" || fields[0] is not ("100644" or "100755"))
        {
            return null;
        }

        return long.TryParse(fields[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size)
            ? size
            : throw new OperationFailedException($"Git returned an unexpected size for '{path}'.");
    }

    public async Task<string> ReadFileAsync(string repositoryRoot, string revision, string path, CancellationToken cancellationToken)
    {
        var result = await RunAsync(repositoryRoot, ["cat-file", "blob", $"{revision}:{path}"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Output : throw Failure($"read '{path}' at {revision}", result);
    }

    private async Task VerifyCommitAsync(string repositoryRoot, string revision, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            repositoryRoot, ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"], cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            return;
        }

        var shallow = await IsShallowAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        throw new InvalidInputException(
            $"Git revision '{revision}' was not found." + (shallow ? " " + ShallowHint : " Check the name, or fetch it first (for example 'git fetch origin')."));
    }

    private async Task<bool> IsShallowAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await RunAsync(repositoryRoot, ["rev-parse", "--is-shallow-repository"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && result.Output.Trim() == "true";
    }

    private static OperationFailedException Failure(string operation, GitResult result)
    {
        var message = result.Error.Trim();
        if (message.Length > 500)
        {
            message = message[..500] + "...";
        }

        return new OperationFailedException($"Git {operation} failed (exit code {result.ExitCode}): {message}");
    }

    private async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        // Never prompt for credentials, never page, and keep messages in English for detection.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["LC_ALL"] = "C";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new OperationFailedException("Git could not be started.");
        }
        catch (Win32Exception ex)
        {
            throw new OperationFailedException(
                $"Git was not found ('{_executable}'). Install Git and make sure it is on the PATH.", ex);
        }

        using (process)
        {
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return new GitResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process already exited.
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new OperationFailedException($"Git did not finish within {_timeout.TotalSeconds:0} seconds.");
            }
        }
    }

    private sealed record GitResult(int ExitCode, string Output, string Error);
}

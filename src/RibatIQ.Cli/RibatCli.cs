using System.CommandLine;

namespace RibatIQ.Cli;

/// <summary>
/// Builds and runs the <c>ribat</c> command tree. Kept separate from <c>Program</c>
/// so the CLI can be exercised in-process by tests with redirected output.
/// </summary>
internal static class RibatCli
{
    public static RootCommand CreateRootCommand()
    {
        // Only implemented behavior is registered here; `scan` and `review`
        // are added in later milestones so `--help` never advertises missing features.
        var root = new RootCommand(
            "RibatIQ - open-source AI code intelligence for .NET. " +
            "Guarding code. Growing intelligence.");

        return root;
    }

    public static async Task<int> InvokeAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parseResult = CreateRootCommand().Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                await error.WriteLineAsync(parseError.Message).ConfigureAwait(false);
            }

            await error.WriteLineAsync("Run 'ribat --help' for usage.").ConfigureAwait(false);
            return ExitCodes.InvalidUsage;
        }

        var configuration = new InvocationConfiguration
        {
            Output = output,
            Error = error,
        };

        return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }
}

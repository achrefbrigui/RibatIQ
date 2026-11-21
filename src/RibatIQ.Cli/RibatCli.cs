using System.CommandLine;

using RibatIQ.Cli.Commands;

namespace RibatIQ.Cli;

/// <summary>
/// Builds and runs the <c>ribat</c> command tree. Kept separate from <c>Program</c>
/// so the CLI can be exercised in-process by tests with redirected output.
/// </summary>
internal static class RibatCli
{
    public static RootCommand CreateRootCommand(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = new RootCommand(
            "RibatIQ - open-source AI code intelligence for .NET. " +
            "Guarding code. Growing intelligence.");

        root.Subcommands.Add(ScanCommand.Create(context));
        root.Subcommands.Add(ReviewCommand.Create(context));

        return root;
    }

    public static async Task<int> InvokeAsync(
        string[] args,
        CliContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        var parseResult = CreateRootCommand(context).Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                await context.Error.WriteLineAsync(parseError.Message).ConfigureAwait(false);
            }

            await context.Error.WriteLineAsync("Run 'ribat --help' for usage.").ConfigureAwait(false);
            return ExitCodes.InvalidUsage;
        }

        var configuration = new InvocationConfiguration
        {
            Output = context.Output,
            Error = context.Error,
        };

        return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }
}

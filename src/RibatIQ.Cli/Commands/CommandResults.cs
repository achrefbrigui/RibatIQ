namespace RibatIQ.Cli.Commands;

/// <summary>Shared command outcomes.</summary>
internal static class CommandResults
{
    /// <summary>
    /// Reported after input validation while the analysis engine is not part of the build yet.
    /// It is an operational failure, never a clean result, so CI cannot mistake it for "no findings".
    /// </summary>
    public static async Task<int> EngineNotAvailableAsync(CliContext context, string commandName, string target)
    {
        await context.Error.WriteLineAsync(
            $"ribat {commandName}: input '{target}' is valid, but the analysis engine is not available in this build yet.")
            .ConfigureAwait(false);
        return ExitCodes.OperationalFailure;
    }
}

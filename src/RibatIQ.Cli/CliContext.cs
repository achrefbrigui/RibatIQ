namespace RibatIQ.Cli;

/// <summary>
/// Process-level dependencies of the CLI, injected so commands can run in-process under test
/// with redirected output, a chosen working directory and a fake environment.
/// </summary>
internal sealed record CliContext(
    TextWriter Output,
    TextWriter Error,
    string CurrentDirectory,
    Func<string, string?> GetEnvironmentVariable)
{
    public static CliContext FromProcess() => new(
        Console.Out,
        Console.Error,
        Environment.CurrentDirectory,
        Environment.GetEnvironmentVariable);
}

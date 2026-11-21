namespace RibatIQ.Cli;

/// <summary>
/// Process exit codes returned by the <c>ribat</c> CLI. These values are a public contract
/// for scripts and CI pipelines; do not renumber them.
/// </summary>
internal static class ExitCodes
{
    /// <summary>Completed; no findings met the configured failure threshold.</summary>
    public const int Success = 0;

    /// <summary>Completed; at least one finding met the configured failure threshold.</summary>
    public const int FindingsAtOrAboveThreshold = 1;

    /// <summary>Invalid usage, configuration, or input.</summary>
    public const int InvalidUsage = 2;

    /// <summary>Operational failure (Git, LLM provider, I/O, ...). Never reported as a clean result.</summary>
    public const int OperationalFailure = 3;
}

using RibatIQ.Domain.Findings;

namespace RibatIQ.Application.Configuration;

/// <summary>
/// Settings contributed by one source (command line, environment, configuration file).
/// <see langword="null"/> means "not set by this source".
/// </summary>
/// <param name="Source">User-facing name of the source, used in error messages.</param>
public sealed record SettingsLayer(string Source)
{
    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool? AiEnabled { get; init; }

    public FailPolicy? FailOn { get; init; }

    public IReadOnlyList<string>? Include { get; init; }

    public IReadOnlyList<string>? Exclude { get; init; }

    /// <summary>Review profile name.</summary>
    public string? Profile { get; init; }

    /// <summary>Rule IDs to enable; a higher layer can disable them again.</summary>
    public IReadOnlyList<string>? EnableRules { get; init; }

    /// <summary>Rule IDs to disable; a higher layer can enable them again.</summary>
    public IReadOnlyList<string>? DisableRules { get; init; }

    public FindingSeverity? MinimumSeverity { get; init; }

    /// <summary>Absolute path of the project instructions file; the source resolves relative paths.</summary>
    public string? InstructionsPath { get; init; }

    public bool? AuditDependencies { get; init; }
}

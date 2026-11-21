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
}

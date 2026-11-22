using System.Text.Json;
using System.Text.Json.Serialization;

using RibatIQ.Application.Configuration;
using RibatIQ.Domain.Findings;

namespace RibatIQ.Cli.Configuration;

/// <summary>
/// Loads the repository configuration file (<c>ribatiq.json</c>). The format is strict: unknown keys
/// are rejected, which catches typos and keeps secrets such as API keys out of committed files.
/// </summary>
/// <example>
/// <code>
/// {
///   "version": 1,
///   "analysis": { "include": ["src/**/*.cs"], "exclude": ["**/Migrations/**"], "failOn": "high" },
///   "ai": { "enabled": true, "provider": "openai", "model": "model-id" },
///   "review": {
///     "profile": "security", "enableRules": ["CLEANCODE.NAMING"], "disableRules": [],
///     "minimumSeverity": "low", "instructions": "docs/review-rules.md", "auditDependencies": true
///   }
/// }
/// </code>
/// </example>
internal static class ConfigFileLoader
{
    public const string DefaultFileName = "ribatiq.json";
    public const int SupportedVersion = 1;
    public const long MaxFileSizeBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Loads <paramref name="explicitPath"/> when given (it must exist), otherwise <see cref="DefaultFileName"/>
    /// from <paramref name="currentDirectory"/> when present. Returns an empty layer when there is no file.
    /// </summary>
    /// <exception cref="ConfigurationException">The file is missing, too large, unreadable or invalid.</exception>
    public static async Task<SettingsLayer> LoadAsync(string? explicitPath, string currentDirectory, CancellationToken cancellationToken)
    {
        string path;
        if (explicitPath is not null)
        {
            path = Path.GetFullPath(explicitPath, currentDirectory);
            if (!File.Exists(path))
            {
                throw new ConfigurationException($"Configuration file '{explicitPath}' does not exist.");
            }
        }
        else
        {
            path = Path.Combine(currentDirectory, DefaultFileName);
            if (!File.Exists(path))
            {
                return new SettingsLayer("defaults");
            }
        }

        var source = $"configuration file '{Path.GetFileName(path)}'";
        ConfigFileModel? model;

        try
        {
            if (new FileInfo(path).Length > MaxFileSizeBytes)
            {
                throw new ConfigurationException($"The {source} exceeds {MaxFileSizeBytes / 1024} KB.");
            }

            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                model = await JsonSerializer.DeserializeAsync<ConfigFileModel>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            var location = ex.LineNumber is { } line ? $" at line {line + 1}" : string.Empty;
            var setting = ex.Path is { Length: > 2 } jsonPath && jsonPath.StartsWith("$.", StringComparison.Ordinal)
                ? jsonPath[2..]
                : null;

            if (setting is not null && ex.Message.Contains("could not be mapped", StringComparison.Ordinal))
            {
                throw new ConfigurationException($"The {source} contains an unknown setting '{setting}'{location}.", ex);
            }

            var problem = setting is null ? DescribeJsonError(ex) : $"invalid value for '{setting}'.";
            throw new ConfigurationException($"The {source} is not valid{location}: {problem}", ex);
        }
        catch (IOException ex)
        {
            throw new ConfigurationException($"The {source} could not be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ConfigurationException($"The {source} could not be read: access denied.", ex);
        }

        return ToLayer(model ?? new ConfigFileModel(), source, Path.GetDirectoryName(path)!);
    }

    private static SettingsLayer ToLayer(ConfigFileModel model, string source, string directory)
    {
        if (model.Version != SupportedVersion)
        {
            throw new ConfigurationException(
                $"The {source} must declare \"version\": {SupportedVersion}" +
                (model.Version is { } v ? $", but declares {v}." : "."));
        }

        FailPolicy? failOn = null;
        if (model.Analysis?.FailOn is { } failOnValue)
        {
            if (!FailPolicy.TryParse(failOnValue, out var parsed))
            {
                throw new ConfigurationException(
                    $"Invalid analysis.failOn value '{failOnValue}' ({source}). Expected one of: {string.Join(", ", FailPolicy.AcceptedValues)}.");
            }

            failOn = parsed;
        }

        FindingSeverity? minimumSeverity = null;
        if (model.Review?.MinimumSeverity is { } severityValue)
        {
            if (!SeverityNames.TryParse(severityValue, out var severity))
            {
                throw new ConfigurationException(
                    $"Invalid review.minimumSeverity value '{severityValue}' ({source}). Expected one of: {string.Join(", ", SeverityNames.All)}.");
            }

            minimumSeverity = severity;
        }

        string? instructions = null;
        if (model.Review?.Instructions is { } instructionsValue)
        {
            if (string.IsNullOrWhiteSpace(instructionsValue) || instructionsValue.Any(char.IsControl))
            {
                throw new ConfigurationException($"Invalid review.instructions path ({source}).");
            }

            // Relative to the configuration file, so the setting means the same from any directory.
            instructions = Path.GetFullPath(instructionsValue.Trim(), directory);
        }

        return new SettingsLayer(source)
        {
            Include = model.Analysis?.Include,
            Exclude = model.Analysis?.Exclude,
            FailOn = failOn,
            AiEnabled = model.Ai?.Enabled,
            Provider = model.Ai?.Provider,
            Model = model.Ai?.Model,
            Profile = model.Review?.Profile,
            EnableRules = model.Review?.EnableRules,
            DisableRules = model.Review?.DisableRules,
            MinimumSeverity = minimumSeverity,
            InstructionsPath = instructions,
            AuditDependencies = model.Review?.AuditDependencies,
        };
    }

    // JsonException messages can quote input; keep only the structural description.
    private static string DescribeJsonError(JsonException ex) =>
        ex.Message.Split(" Path:", 2, StringSplitOptions.None)[0].Split(" LineNumber:", 2, StringSplitOptions.None)[0];

    private sealed record ConfigFileModel
    {
        public int? Version { get; init; }

        public AnalysisSection? Analysis { get; init; }

        public AiSection? Ai { get; init; }

        public ReviewSection? Review { get; init; }
    }

    private sealed record AnalysisSection
    {
        public IReadOnlyList<string>? Include { get; init; }

        public IReadOnlyList<string>? Exclude { get; init; }

        public string? FailOn { get; init; }
    }

    private sealed record ReviewSection
    {
        public string? Profile { get; init; }

        public IReadOnlyList<string>? EnableRules { get; init; }

        public IReadOnlyList<string>? DisableRules { get; init; }

        public string? MinimumSeverity { get; init; }

        public string? Instructions { get; init; }

        public bool? AuditDependencies { get; init; }
    }

    private sealed record AiSection
    {
        public bool? Enabled { get; init; }

        public string? Provider { get; init; }

        public string? Model { get; init; }
    }
}

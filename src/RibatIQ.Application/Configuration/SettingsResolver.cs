namespace RibatIQ.Application.Configuration;

/// <summary>
/// Merges settings layers into <see cref="RibatSettings"/>.
/// </summary>
/// <remarks>
/// Layers are given highest precedence first (command line, environment, configuration file).
/// For single values the first layer that sets a value wins; otherwise defaults apply.
/// <list type="bullet">
/// <item>AI analysis is enabled only when a provider is configured, unless a layer explicitly disables it.</item>
/// <item><c>Include</c> comes from the highest layer that sets a non-empty list.</item>
/// <item><c>Exclude</c> is additive: <see cref="DefaultExcludes"/> plus every layer's exclusions, so a
/// higher layer can never re-include build output or the Git directory.</item>
/// </list>
/// </remarks>
public static class SettingsResolver
{
    public const int MaxGlobLength = 512;

    public static IReadOnlyList<string> DefaultExcludes { get; } = ["**/bin/**", "**/obj/**", "**/.git/**"];

    /// <exception cref="ConfigurationException">A layer holds an invalid value or the layers are inconsistent.</exception>
    public static RibatSettings Resolve(IReadOnlyList<SettingsLayer> layersByPrecedence)
    {
        ArgumentNullException.ThrowIfNull(layersByPrecedence);

        foreach (var layer in layersByPrecedence)
        {
            Validate(layer);
        }

        var providerLayer = layersByPrecedence.FirstOrDefault(l => l.Provider is not null);
        var provider = providerLayer is null ? null : LlmProviders.Normalize(providerLayer.Provider);
        var model = layersByPrecedence.Select(l => l.Model?.Trim()).FirstOrDefault(m => !string.IsNullOrEmpty(m));

        var aiLayer = layersByPrecedence.FirstOrDefault(l => l.AiEnabled is not null);
        var aiEnabled = aiLayer?.AiEnabled ?? provider is not null;

        if (aiEnabled && provider is null)
        {
            throw new ConfigurationException(
                $"AI analysis is enabled ({aiLayer!.Source}) but no provider is configured. " +
                $"Set a provider ({string.Join(", ", LlmProviders.All)}) or disable AI analysis.");
        }

        var failOn = layersByPrecedence.Select(l => l.FailOn).FirstOrDefault(f => f is not null) ?? FailPolicy.Never;

        var include = layersByPrecedence
            .Select(l => l.Include)
            .FirstOrDefault(globs => globs is { Count: > 0 })?
            .Select(g => g.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        var exclude = DefaultExcludes
            .Concat(layersByPrecedence.Reverse().SelectMany(l => l.Exclude ?? []).Select(g => g.Trim()))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new RibatSettings(
            aiEnabled,
            aiEnabled ? provider : null,
            aiEnabled ? model : null,
            failOn,
            include,
            exclude);
    }

    private static void Validate(SettingsLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (layer.Provider is not null && LlmProviders.Normalize(layer.Provider) is null)
        {
            throw new ConfigurationException(
                $"Unknown provider '{layer.Provider.Trim()}' ({layer.Source}). Expected one of: {string.Join(", ", LlmProviders.All)}.");
        }

        ValidateGlobs(layer.Include, "include", layer.Source);
        ValidateGlobs(layer.Exclude, "exclude", layer.Source);
    }

    private static void ValidateGlobs(IReadOnlyList<string>? globs, string kind, string source)
    {
        foreach (var glob in globs ?? [])
        {
            if (string.IsNullOrWhiteSpace(glob) || glob.Length > MaxGlobLength || glob.Any(char.IsControl))
            {
                throw new ConfigurationException(
                    $"Invalid {kind} pattern ({source}): patterns must be non-empty, at most {MaxGlobLength} characters, without control characters.");
            }
        }
    }
}

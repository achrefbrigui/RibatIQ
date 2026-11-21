using RibatIQ.Domain.Findings;

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
/// <item>Rule enable/disable lists are applied from the lowest layer to the highest; within one layer a rule cannot be both.</item>
/// <item><c>Exclude</c> is additive: <see cref="DefaultExcludes"/> plus every layer's exclusions, so a
/// higher layer can never re-include build output or the Git directory.</item>
/// </list>
/// </remarks>
public static class SettingsResolver
{
    public const int MaxGlobLength = 512;
    public const int MaxRuleIdLength = 100;

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

        var (enableRules, disableRules) = MergeRules(layersByPrecedence);

        return new RibatSettings(
            aiEnabled,
            aiEnabled ? provider : null,
            aiEnabled ? model : null,
            failOn,
            include,
            exclude)
        {
            Profile = layersByPrecedence.Select(l => l.Profile?.Trim()).FirstOrDefault(p => !string.IsNullOrEmpty(p)),
            EnableRules = enableRules,
            DisableRules = disableRules,
            MinimumSeverity = layersByPrecedence.Select(l => l.MinimumSeverity).FirstOrDefault(s => s is not null) ?? FindingSeverity.Info,
            InstructionsPath = layersByPrecedence.Select(l => l.InstructionsPath).FirstOrDefault(p => p is not null),
            AuditDependencies = layersByPrecedence.Select(l => l.AuditDependencies).FirstOrDefault(a => a is not null) ?? false,
        };
    }

    /// <summary>
    /// Applies the layers from the lowest precedence to the highest, so a higher layer can enable a rule a
    /// lower one disabled, and the other way round.
    /// </summary>
    private static (string[] Enable, string[] Disable) MergeRules(IReadOnlyList<SettingsLayer> layersByPrecedence)
    {
        var enable = new List<string>();
        var disable = new List<string>();

        foreach (var layer in layersByPrecedence.Reverse())
        {
            foreach (var rule in layer.EnableRules ?? [])
            {
                var id = rule.Trim().ToUpperInvariant();
                disable.Remove(id);
                if (!enable.Contains(id))
                {
                    enable.Add(id);
                }
            }

            foreach (var rule in layer.DisableRules ?? [])
            {
                var id = rule.Trim().ToUpperInvariant();
                enable.Remove(id);
                if (!disable.Contains(id))
                {
                    disable.Add(id);
                }
            }
        }

        return ([.. enable], [.. disable]);
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

        if (layer.MinimumSeverity is { } severity && !Enum.IsDefined(severity))
        {
            throw new ConfigurationException($"Invalid minimum severity ({layer.Source}).");
        }

        if (layer.InstructionsPath is { } path && (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
        {
            throw new ConfigurationException($"Invalid instructions path ({layer.Source}).");
        }

        var enable = ValidateRules(layer.EnableRules, "enable", layer.Source);
        var disable = ValidateRules(layer.DisableRules, "disable", layer.Source);
        var conflict = enable.Intersect(disable, StringComparer.Ordinal).Order(StringComparer.Ordinal).FirstOrDefault();
        if (conflict is not null)
        {
            throw new ConfigurationException($"Rule '{conflict}' is both enabled and disabled ({layer.Source}).");
        }
    }

    /// <summary>Checks the shape only; whether the rule exists is checked against the catalogue later.</summary>
    private static string[] ValidateRules(IReadOnlyList<string>? rules, string kind, string source)
    {
        foreach (var rule in rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule) || rule.Length > MaxRuleIdLength || rule.Any(char.IsControl))
            {
                throw new ConfigurationException(
                    $"Invalid rule to {kind} ({source}): rule IDs must be non-empty, at most {MaxRuleIdLength} characters, without control characters.");
            }
        }

        return [.. (rules ?? []).Select(r => r.Trim().ToUpperInvariant())];
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

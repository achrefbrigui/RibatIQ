namespace RibatIQ.Domain.Results;

/// <summary>
/// Describes how an analysis was run. Never contains secrets, prompts or source code.
/// </summary>
public sealed record AnalysisMetadata
{
    public AnalysisMetadata(string toolVersion, bool aiEnabled, string? provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(toolVersion))
        {
            throw new ArgumentException("Tool version must not be empty.", nameof(toolVersion));
        }

        provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();

        if (aiEnabled && provider is null)
        {
            throw new ArgumentException("A provider is required when AI analysis is enabled.", nameof(provider));
        }

        if (!aiEnabled && (provider is not null || model is not null))
        {
            throw new ArgumentException("Provider and model must be omitted when AI analysis is disabled.", nameof(aiEnabled));
        }

        ToolVersion = toolVersion.Trim();
        AiEnabled = aiEnabled;
        Provider = provider;
        Model = model;
    }

    /// <summary>Version of RibatIQ that produced the result.</summary>
    public string ToolVersion { get; }

    /// <summary>Whether AI analysis was requested for this run.</summary>
    public bool AiEnabled { get; }

    /// <summary>LLM provider ID (for example <c>openai</c>), when AI analysis was enabled.</summary>
    public string? Provider { get; }

    /// <summary>Model identifier used, when known.</summary>
    public string? Model { get; }

    public static AnalysisMetadata WithoutAi(string toolVersion) => new(toolVersion, aiEnabled: false, provider: null, model: null);
}

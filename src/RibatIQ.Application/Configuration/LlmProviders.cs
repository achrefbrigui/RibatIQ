namespace RibatIQ.Application.Configuration;

/// <summary>Identifiers of the LLM providers RibatIQ can be configured with.</summary>
public static class LlmProviders
{
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";

    public static IReadOnlyList<string> All { get; } = [OpenAi, Anthropic];

    /// <summary>Returns the canonical provider ID for <paramref name="value"/>, ignoring case, or <see langword="null"/> when unknown.</summary>
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return All.FirstOrDefault(id => string.Equals(id, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

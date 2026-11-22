using RibatIQ.Application.Configuration;

namespace RibatIQ.Infrastructure.Llm;

/// <summary>
/// A service that speaks the OpenAI Chat Completions protocol, used by <see cref="OpenAiProvider"/>.
/// Endpoints are fixed here: environment variables never redirect where source code is sent.
/// </summary>
/// <param name="ProviderId">Canonical provider ID (see <see cref="LlmProviders"/>).</param>
/// <param name="DisplayName">Name used in messages.</param>
/// <param name="ApiKeyVariable">Environment variable holding the API key.</param>
/// <param name="DefaultModel">Model used when none is configured.</param>
/// <param name="Endpoint">API base address; <see langword="null"/> for OpenAI's official endpoint.</param>
/// <param name="JsonObjectFallback">
/// Whether a request rejected with HTTP 400 is retried once with plain JSON mode, the schema given in the
/// prompt instead, for services whose support of strict JSON schemas is partial. The answer is still
/// validated like any other.
/// </param>
public sealed record OpenAiCompatibleService(
    string ProviderId,
    string DisplayName,
    string ApiKeyVariable,
    string DefaultModel,
    Uri? Endpoint,
    bool JsonObjectFallback)
{
    /// <summary>OpenAI's official API.</summary>
    public static OpenAiCompatibleService OpenAi { get; } = new(
        LlmProviders.OpenAi, "OpenAI", "OPENAI_API_KEY", "gpt-6.2-astra", Endpoint: null, JsonObjectFallback: false);

    /// <summary>Google's Gemini API through its OpenAI-compatible endpoint.</summary>
    public static OpenAiCompatibleService Gemini { get; } = new(
        LlmProviders.Gemini,
        "Gemini",
        "GEMINI_API_KEY",
        "gemini-4-hydrogen",
        new Uri("https://generativelanguage.googleapis.com/v1beta/openai"),
        JsonObjectFallback: true);
}

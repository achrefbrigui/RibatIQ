namespace RibatIQ.Application.Ai;

/// <summary>
/// A large language model behind a vendor-neutral contract. Implementations live in Infrastructure
/// and keep every vendor SDK type internal.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Provider identifier, for example <c>openai</c>.</summary>
    string ProviderId { get; }

    /// <summary>Model identifier the provider sends requests to.</summary>
    string Model { get; }

    /// <summary>Sends one request and returns the model's text output.</summary>
    /// <exception cref="LlmProviderException">The provider failed, refused, or returned an unusable response.</exception>
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken);
}

/// <summary>Creates providers from configuration (provider ID, model, credentials from the environment).</summary>
public interface ILlmProviderFactory
{
    /// <exception cref="Configuration.ConfigurationException">Credentials are missing.</exception>
    /// <exception cref="OperationFailedException">The provider is not available.</exception>
    ILlmProvider Create(string providerId, string? model);
}

/// <summary>A single-turn request with a JSON response format.</summary>
/// <param name="SystemPrompt">Operator instructions. Never contains repository content.</param>
/// <param name="UserPrompt">The task and the (untrusted) repository content.</param>
/// <param name="ResponseSchema">JSON schema the response must follow, when the provider supports structured outputs.</param>
/// <param name="MaxOutputTokens">Upper bound for generated tokens.</param>
public sealed record LlmRequest(string SystemPrompt, string UserPrompt, LlmJsonSchema ResponseSchema, int MaxOutputTokens);

/// <summary>A named JSON schema for structured outputs.</summary>
/// <param name="Name">Schema name (letters, digits, '_' and '-').</param>
/// <param name="SchemaJson">The JSON schema document.</param>
public sealed record LlmJsonSchema(string Name, string SchemaJson);

/// <summary>The model's raw text output. Always untrusted: it is parsed and validated before use.</summary>
/// <param name="Text">Generated text.</param>
/// <param name="Model">Model that actually served the request, as reported by the provider.</param>
/// <param name="Usage">Token usage, when reported.</param>
public sealed record LlmResponse(string Text, string Model, LlmUsage? Usage);

/// <summary>Token usage of one request.</summary>
public sealed record LlmUsage(long InputTokens, long OutputTokens);

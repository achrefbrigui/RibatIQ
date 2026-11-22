using RibatIQ.Application;
using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;
using RibatIQ.Infrastructure.Llm;

namespace RibatIQ.Cli;

/// <summary>
/// Maps provider IDs to the adapters compiled into this build. Each entry receives the requested
/// model (or <see langword="null"/> for the adapter's default) and reads its credentials itself.
/// </summary>
internal sealed class LlmProviderRegistry : ILlmProviderFactory
{
    private readonly IReadOnlyDictionary<string, Func<string?, ILlmProvider>> _providers;

    public LlmProviderRegistry(IReadOnlyDictionary<string, Func<string?, ILlmProvider>> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <summary>The providers available in this build.</summary>
    public static LlmProviderRegistry CreateDefault(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new LlmProviderRegistry(new Dictionary<string, Func<string?, ILlmProvider>>(StringComparer.Ordinal)
        {
            [LlmProviders.OpenAi] = model => OpenAiProvider.FromEnvironment(context.GetEnvironmentVariable, model),
            [LlmProviders.Anthropic] = model => AnthropicProvider.FromEnvironment(context.GetEnvironmentVariable, model),
            [LlmProviders.Gemini] = model => OpenAiProvider.FromEnvironment(
                context.GetEnvironmentVariable, model, service: OpenAiCompatibleService.Gemini),
        });
    }

    public ILlmProvider Create(string providerId, string? model) =>
        _providers.TryGetValue(providerId, out var create)
            ? create(model)
            : throw new OperationFailedException(
                $"AI provider '{providerId}' is not available in this build yet. " +
                "Run with --no-ai or remove the provider setting to run static analysis only.");
}

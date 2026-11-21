using RibatIQ.Application.Ai;
using RibatIQ.Application.Sources;
using RibatIQ.Infrastructure.Llm;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// Calls the real provider APIs. Skipped unless <c>RIBATIQ_LIVE_TESTS=1</c> and the provider's API key
/// are set, so the standard test suite never needs credentials or network access. These calls are billed.
/// </summary>
public sealed class LiveProviderTests
{
    private const string Code = """
        public static class Parser
        {
            public static int ParsePort(string value)
            {
                try { return int.Parse(value); }
                catch (System.Exception) { }
                return 0;
            }
        }
        """;

    [Fact]
    public async Task OpenAi_ReturnsSchemaValidFindings()
    {
        var key = RequireLive(OpenAiProvider.ApiKeyVariable);

        await AssertProviderProducesValidResponseAsync(new OpenAiProvider(key, Environment.GetEnvironmentVariable("RIBATIQ_LIVE_OPENAI_MODEL")));
    }

    [Fact]
    public async Task Anthropic_ReturnsSchemaValidFindings()
    {
        var key = RequireLive(AnthropicProvider.ApiKeyVariable);

        using var provider = new AnthropicProvider(key, Environment.GetEnvironmentVariable("RIBATIQ_LIVE_ANTHROPIC_MODEL"));
        await AssertProviderProducesValidResponseAsync(provider);
    }

    internal static string RequireLive(string apiKeyVariable)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("RIBATIQ_LIVE_TESTS") == "1", "Set RIBATIQ_LIVE_TESTS=1 to run live provider tests.");
        var key = Environment.GetEnvironmentVariable(apiKeyVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), $"{apiKeyVariable} is not set.");
        return key!;
    }

    internal static async Task AssertProviderProducesValidResponseAsync(ILlmProvider provider)
    {
        var result = await new AiAnalyzer().AnalyzeScanAsync(
            provider, [new SourceDocument("src/Parser.cs", Code)], TestContext.Current.CancellationToken);

        // The response parsed and validated; every finding points at code that was sent.
        Assert.All(result.Findings, f => Assert.Equal("src/Parser.cs", f.FilePath));
        Assert.NotNull(result.Usage);
    }
}

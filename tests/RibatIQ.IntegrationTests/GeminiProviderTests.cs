using System.Net;
using System.Text.Json;

using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;
using RibatIQ.Infrastructure.Llm;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// Gemini through its OpenAI-compatible endpoint: fixed destination, its own key variable, and a one-time
/// fallback to plain JSON mode when strict schemas are rejected. Shared behavior is in
/// <see cref="GeminiProviderContractTests"/>.
/// </summary>
public sealed class GeminiProviderTests
{
    private const string ApiKey = "gemini-test-0123456789abcdef";

    private static readonly LlmRequest Request = new(
        "SYSTEM PROMPT", "USER PROMPT", AiPromptBuilder.ResponseSchema, MaxOutputTokens: 1234);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Generate_CallsTheGeminiEndpoint_WithAStrictSchemaFirst()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, OpenAiProviderTests.Completion("""{"findings":[]}"""));

        await CreateProvider(handler).GenerateAsync(Request, Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", sent.Uri.ToString());
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);

        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal("gemini-4-hydrogen", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SchemaRejected_RetriesOnceInJsonMode_WithTheSchemaInThePrompt()
    {
        var handler = new StubHttpHandler()
            .Respond(HttpStatusCode.BadRequest, OpenAiProviderTests.Error("Invalid JSON payload: response_format is not supported."))
            .Respond(HttpStatusCode.OK, OpenAiProviderTests.Completion("""{"findings":[]}"""));

        var response = await CreateProvider(handler).GenerateAsync(Request, Ct);

        Assert.Equal("""{"findings":[]}""", response.Text);
        Assert.Equal(2, handler.Requests.Count);

        using var retry = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal("json_object", retry.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        var system = retry.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.StartsWith("SYSTEM PROMPT", system, StringComparison.Ordinal);
        Assert.Contains(AiPromptBuilder.ResponseSchema.SchemaJson, system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterAFallback_LaterRequestsUseJsonModeDirectly()
    {
        var handler = new StubHttpHandler()
            .Respond(HttpStatusCode.BadRequest, OpenAiProviderTests.Error("response_format is not supported."))
            .Respond(HttpStatusCode.OK, OpenAiProviderTests.Completion("""{"findings":[]}"""))
            .Respond(HttpStatusCode.OK, OpenAiProviderTests.Completion("""{"findings":[]}"""));
        var provider = CreateProvider(handler);

        await provider.GenerateAsync(Request, Ct);
        await provider.GenerateAsync(Request, Ct);

        Assert.Equal(3, handler.Requests.Count);
        using var third = JsonDocument.Parse(handler.Requests[2].Body);
        Assert.Equal("json_object", third.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAi_DoesNotFallBack_On400()
    {
        var handler = new StubHttpHandler()
            .Respond(HttpStatusCode.BadRequest, OpenAiProviderTests.Error("Bad request."))
            .Respond(HttpStatusCode.OK, OpenAiProviderTests.Completion("""{"findings":[]}"""));
        var provider = new OpenAiProvider(ApiKey, options: new OpenAiProviderOptions { HttpClient = new HttpClient(handler), MaxRetries = 0 });

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => provider.GenerateAsync(Request, Ct));

        Assert.Equal(LlmErrorKind.InvalidRequest, ex.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Defaults_UseTheGeminiIdAndModel()
    {
        var provider = new OpenAiProvider(ApiKey, service: OpenAiCompatibleService.Gemini);

        Assert.Equal(LlmProviders.Gemini, provider.ProviderId);
        Assert.Equal("gemini-4-hydrogen", provider.Model);
    }

    [Fact]
    public void FromEnvironment_ReadsOnlyTheGeminiKey()
    {
        var environment = new Dictionary<string, string> { ["OPENAI_API_KEY"] = "sk-openai" };

        var ex = Assert.Throws<ConfigurationException>(() =>
            OpenAiProvider.FromEnvironment(environment.GetValueOrDefault, model: null, service: OpenAiCompatibleService.Gemini));

        Assert.Contains("GEMINI_API_KEY", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Gemini provider", ex.Message, StringComparison.Ordinal);
    }

    private static OpenAiProvider CreateProvider(StubHttpHandler handler) =>
        new(ApiKey, options: new OpenAiProviderOptions { HttpClient = new HttpClient(handler), MaxRetries = 0 }, service: OpenAiCompatibleService.Gemini);
}

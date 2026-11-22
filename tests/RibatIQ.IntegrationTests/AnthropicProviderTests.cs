using System.Net;
using System.Text.Json;

using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;
using RibatIQ.Infrastructure.Llm;

namespace RibatIQ.IntegrationTests;

/// <summary>Anthropic-specific request shape and behavior, against a stubbed HTTP transport.</summary>
public sealed class AnthropicProviderTests
{
    private const string ApiKey = "sk-ant-test-0123456789abcdef";

    private static readonly LlmRequest Request = new(
        "SYSTEM PROMPT", "USER PROMPT", AiPromptBuilder.ResponseSchema, MaxOutputTokens: 1234);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Generate_SendsMessagesRequestWithStructuredOutputAndHighEffort()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Message("""{"findings":[]}"""));

        await CreateProvider(handler).GenerateAsync(Request, Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/v1/messages", sent.Uri.AbsolutePath, StringComparison.Ordinal);
        // The SDK sends an explicit API key as a bearer token and then reads no credential from the environment.
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);

        // The endpoint is pinned: ambient variables such as ANTHROPIC_BASE_URL do not redirect source code.
        Assert.Equal("api.anthropic.com", sent.Uri.Host);
        Assert.True(sent.Headers.ContainsKey("anthropic-version"));

        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Equal(1234, root.GetProperty("max_tokens").GetInt32());
        Assert.Contains("SYSTEM PROMPT", root.GetProperty("system").ToString(), StringComparison.Ordinal);

        var message = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Contains("USER PROMPT", message.GetProperty("content").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SYSTEM PROMPT", message.GetProperty("content").ToString(), StringComparison.Ordinal);

        var output = root.GetProperty("output_config");
        Assert.Equal("high", output.GetProperty("effort").GetString());
        Assert.Equal("json_schema", output.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("object", output.GetProperty("format").GetProperty("schema").GetProperty("type").GetString());

        Assert.False(root.TryGetProperty("thinking", out _));
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Generate_DefaultModel_RequestsServerSideRefusalFallback()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Message("{}"));

        var provider = CreateProvider(handler);
        await provider.GenerateAsync(Request, Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.True(provider.UsesRefusalFallback);
        Assert.Contains("server-side-fallback-2026-07-01", sent.Headers["anthropic-beta"], StringComparison.Ordinal);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal("default", body.RootElement.GetProperty("fallbacks").GetString());
    }

    [Fact]
    public async Task Generate_ModelWithoutFallbackSupport_DoesNotRequestIt()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Message("{}"));

        var provider = CreateProvider(handler, model: "claude-haiku-5-5");
        await provider.GenerateAsync(Request, Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.False(provider.UsesRefusalFallback);
        Assert.False(sent.Headers.TryGetValue("anthropic-beta", out var beta) && beta.Contains("server-side-fallback", StringComparison.Ordinal));
        using var body = JsonDocument.Parse(sent.Body);
        Assert.False(body.RootElement.TryGetProperty("fallbacks", out _));
        Assert.Equal("claude-haiku-5-5", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Generate_IgnoresThinkingBlocks_AndReportsTheModelThatAnswered()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "msg_01",
            type = "message",
            role = "assistant",
            model = "claude-opus-4-8",
            content = new object[]
            {
                new { type = "thinking", thinking = "", signature = "sig" },
                new { type = "text", text = """{"findings":[]}""" },
            },
            stop_reason = "end_turn",
            stop_sequence = (string?)null,
            usage = new { input_tokens = 10, output_tokens = 5 },
        });
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, json);

        var response = await CreateProvider(handler).GenerateAsync(Request, Ct);

        Assert.Equal("""{"findings":[]}""", response.Text);
        Assert.Equal("claude-opus-4-8", response.Model);
    }

    [Fact]
    public async Task Generate_Overloaded529_IsServiceUnavailable()
    {
        var handler = new StubHttpHandler().Respond((HttpStatusCode)529, Error("Overloaded", "overloaded_error"));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));

        Assert.Equal(LlmErrorKind.ServiceUnavailable, ex.Kind);
    }

    [Fact]
    public async Task Generate_TransientError_IsRetried_ThenSucceeds()
    {
        var handler = new StubHttpHandler()
            .Respond((HttpStatusCode)529, Error("Overloaded", "overloaded_error"), ("retry-after-ms", "1"), ("retry-after", "0"))
            .Respond(HttpStatusCode.OK, Message("""{"findings":[]}"""));

        var response = await CreateProvider(handler, maxRetries: 1).GenerateAsync(Request, Ct);

        Assert.Equal("""{"findings":[]}""", response.Text);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Generate_AuthenticationError_IsNotRetried()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.Unauthorized, Error("invalid x-api-key", "authentication_error"));

        await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler, maxRetries: 2).GenerateAsync(Request, Ct));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Defaults_AndEnvironment()
    {
        using var provider = new AnthropicProvider(ApiKey);
        Assert.Equal("claude-opus-5-5", provider.Model);
        Assert.Equal(LlmProviders.Anthropic, provider.ProviderId);

        var ex = Assert.Throws<ConfigurationException>(() => AnthropicProvider.FromEnvironment(_ => null, model: null));
        Assert.Contains(AnthropicProvider.ApiKeyVariable, ex.Message, StringComparison.Ordinal);

        var asked = new List<string>();
        using var fromEnv = AnthropicProvider.FromEnvironment(name => { asked.Add(name); return ApiKey; }, model: "claude-sonnet-5-5");
        Assert.Equal([AnthropicProvider.ApiKeyVariable], asked);
        Assert.Equal("claude-sonnet-5-5", fromEnv.Model);
    }

    internal static string Message(string? text, string stopReason = "end_turn", string model = "claude-opus-5-5") =>
        JsonSerializer.Serialize(new
        {
            id = "msg_01",
            type = "message",
            role = "assistant",
            model,
            content = text is null ? Array.Empty<object>() : [new { type = "text", text }],
            stop_reason = stopReason,
            stop_sequence = (string?)null,
            stop_details = stopReason == "refusal"
                ? new { type = "refusal", category = "cyber", explanation = "Declined." }
                : null,
            usage = new { input_tokens = 321, output_tokens = 45 },
        });

    internal static string Error(string message, string type = "invalid_request_error") =>
        JsonSerializer.Serialize(new { type = "error", error = new { type, message }, request_id = "req_test" });

    private static AnthropicProvider CreateProvider(StubHttpHandler handler, string? model = null, int maxRetries = 0) =>
        new(ApiKey, model, new AnthropicProviderOptions { HttpClient = new HttpClient(handler), MaxRetries = maxRetries });
}

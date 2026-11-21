using System.Net;
using System.Text.Json;

using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;
using RibatIQ.Infrastructure.Llm;

namespace RibatIQ.IntegrationTests;

/// <summary>OpenAI adapter against a stubbed HTTP transport: request shape, response and error mapping.</summary>
public sealed class OpenAiProviderTests
{
    private const string ApiKey = "sk-test-0123456789abcdef";

    private static readonly LlmRequest Request = new(
        "SYSTEM PROMPT", "USER PROMPT", AiPromptBuilder.ResponseSchema, MaxOutputTokens: 1234);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Generate_SendsChatCompletionWithStrictJsonSchema()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Completion("""{"findings":[]}"""));

        await CreateProvider(handler, model: "gpt-test").GenerateAsync(Request, Ct);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/chat/completions", sent.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);

        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal("gpt-test", root.GetProperty("model").GetString());
        Assert.Equal(1234, root.GetProperty("max_completion_tokens").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("SYSTEM PROMPT", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("USER PROMPT", messages[1].GetProperty("content").GetString());

        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schema = format.GetProperty("json_schema");
        Assert.Equal(AiPromptBuilder.SchemaName, schema.GetProperty("name").GetString());
        Assert.True(schema.GetProperty("strict").GetBoolean());
        Assert.Equal("object", schema.GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Generate_ReturnsTextModelAndUsage()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Completion("""{"findings":[]}""", model: "gpt-6.2-astra-2026-10-01"));

        var response = await CreateProvider(handler).GenerateAsync(Request, Ct);

        Assert.Equal("""{"findings":[]}""", response.Text);
        Assert.Equal("gpt-6.2-astra-2026-10-01", response.Model);
        Assert.Equal(new LlmUsage(321, 45), response.Usage);
    }

    [Fact]
    public void Defaults_UseRecommendedModel_AndProviderId()
    {
        var provider = new OpenAiProvider(ApiKey);

        Assert.Equal(OpenAiProvider.DefaultModel, provider.Model);
        Assert.Equal("gpt-6.2-astra", provider.Model);
        Assert.Equal(LlmProviders.OpenAi, provider.ProviderId);
    }

    [Fact]
    public void FromEnvironment_WithoutKey_IsAConfigurationError()
    {
        var ex = Assert.Throws<ConfigurationException>(() => OpenAiProvider.FromEnvironment(_ => "  ", model: null));

        Assert.Contains(OpenAiProvider.ApiKeyVariable, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromEnvironment_ReadsOnlyTheOpenAiKey()
    {
        var asked = new List<string>();

        var provider = OpenAiProvider.FromEnvironment(name => { asked.Add(name); return ApiKey; }, model: "custom");

        Assert.Equal([OpenAiProvider.ApiKeyVariable], asked);
        Assert.Equal("custom", provider.Model);
    }

    [Fact]
    public async Task Generate_Refusal_IsReportedAsRefused()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Completion(content: null, refusal: "I can't help with that."));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));

        Assert.Equal(LlmErrorKind.Refused, ex.Kind);
    }

    [Theory]
    [InlineData("length", LlmErrorKind.Truncated)]
    [InlineData("content_filter", LlmErrorKind.Refused)]
    public async Task Generate_IncompleteResponse_IsAFailure(string finishReason, LlmErrorKind expected)
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Completion("""{"findings":[""", finishReason: finishReason));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task Generate_EmptyContent_IsMalformed()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Completion(content: ""));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));

        Assert.Equal(LlmErrorKind.MalformedResponse, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, LlmErrorKind.PermissionDenied)]
    [InlineData(HttpStatusCode.NotFound, LlmErrorKind.ModelNotFound)]
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.InvalidRequest)]
    public async Task Generate_NonTransientErrors_AreMapped_AndNeverRetried(HttpStatusCode status, LlmErrorKind expected)
    {
        var handler = new StubHttpHandler().Respond(status, Error("Something is wrong."));

        // Default SDK retry policy: non-transient failures must not be retried.
        var provider = new OpenAiProvider(ApiKey, options: new OpenAiProviderOptions { HttpClient = new HttpClient(handler) });
        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => provider.GenerateAsync(Request, Ct));

        Assert.Equal(expected, ex.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Generate_TransientError_IsRetried_ThenSucceeds()
    {
        var handler = new StubHttpHandler()
            .Respond(HttpStatusCode.ServiceUnavailable, Error("Overloaded."), ("retry-after-ms", "1"))
            .Respond(HttpStatusCode.OK, Completion("""{"findings":[]}"""));

        var response = await CreateProvider(handler, maxRetries: 1).GenerateAsync(Request, Ct);

        Assert.Equal("""{"findings":[]}""", response.Text);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, LlmErrorKind.ServiceUnavailable)]
    public async Task Generate_TransientErrorsAfterRetries_AreMapped(HttpStatusCode status, LlmErrorKind expected)
    {
        var handler = new StubHttpHandler().Respond(status, Error("Try later."));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler, maxRetries: 0).GenerateAsync(Request, Ct));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task Generate_ErrorMessages_NeverContainTheApiKey()
    {
        var handler = new StubHttpHandler()
            .Respond(HttpStatusCode.Unauthorized, Error($"Incorrect API key provided: {ApiKey}."))
            .Respond(HttpStatusCode.BadRequest, Error($"Echo of {ApiKey} in a validation error."));

        var auth = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));
        var invalid = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));

        Assert.DoesNotContain(ApiKey, auth.Message, StringComparison.Ordinal);
        Assert.Contains(OpenAiProvider.ApiKeyVariable, auth.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, invalid.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_NetworkFailure_IsMapped()
    {
        var handler = new StubHttpHandler().Throw(new HttpRequestException("No such host is known."));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler, maxRetries: 0).GenerateAsync(Request, Ct));

        Assert.Equal(LlmErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Generate_Cancellation_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, Completion("{}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProvider(handler).GenerateAsync(Request, cancelled.Token));
    }

    internal static string Completion(string? content, string? refusal = null, string finishReason = "stop", string model = "gpt-6.2-astra") =>
        JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1_763_700_000,
            model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content, refusal },
                    finish_reason = finishReason,
                    logprobs = (object?)null,
                },
            },
            usage = new { prompt_tokens = 321, completion_tokens = 45, total_tokens = 366 },
        });

    internal static string Error(string message) =>
        JsonSerializer.Serialize(new { error = new { message, type = "invalid_request_error", code = (string?)null } });

    private static OpenAiProvider CreateProvider(StubHttpHandler handler, string? model = null, int maxRetries = 0) =>
        new(ApiKey, model, new OpenAiProviderOptions { HttpClient = new HttpClient(handler), MaxRetries = maxRetries });
}

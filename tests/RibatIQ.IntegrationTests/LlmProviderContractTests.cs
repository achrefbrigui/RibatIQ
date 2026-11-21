using System.Net;

using RibatIQ.Application.Ai;
using RibatIQ.Infrastructure.Llm;

namespace RibatIQ.IntegrationTests;

/// <summary>
/// Behavior every <see cref="ILlmProvider"/> adapter must have, verified offline against stubbed HTTP.
/// Each adapter supplies its wire formats; the assertions are shared.
/// </summary>
public abstract class LlmProviderContractTests
{
    protected const string ApiKey = "sk-contract-0123456789abcdef";

    protected static readonly LlmRequest Request = new(
        "SYSTEM PROMPT", "USER PROMPT", AiPromptBuilder.ResponseSchema, MaxOutputTokens: 2048);

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract string ApiKeyVariable { get; }

    /// <summary>Creates the adapter on top of <paramref name="handler"/>, without retries.</summary>
    private protected abstract ILlmProvider CreateProvider(StubHttpHandler handler);

    /// <summary>A successful response with model "served-model" and usage 321 input / 45 output tokens.</summary>
    protected abstract string SuccessBody(string text);

    protected abstract string RefusalBody();

    protected abstract string TruncatedBody();

    protected abstract string ErrorBody(string message);

    [Fact]
    public async Task Success_ReturnsTextServedModelAndUsage()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, SuccessBody("""{"findings":[]}"""));

        var response = await CreateProvider(handler).GenerateAsync(Request, Ct);

        Assert.Equal("""{"findings":[]}""", response.Text);
        Assert.Equal("served-model", response.Model);
        Assert.Equal(new LlmUsage(321, 45), response.Usage);
    }

    [Fact]
    public async Task Request_CarriesPromptsAndSchema_ButNeverTheApiKeyInTheBody()
    {
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, SuccessBody("{}"));

        await CreateProvider(handler).GenerateAsync(Request, Ct);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("SYSTEM PROMPT", body, StringComparison.Ordinal);
        Assert.Contains("USER PROMPT", body, StringComparison.Ordinal);
        Assert.Contains("startLine", body, StringComparison.Ordinal);
        Assert.Contains("2048", body, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refusal_IsAFailure_NotAnEmptyResult() =>
        Assert.Equal(LlmErrorKind.Refused, await FailureKindAsync(HttpStatusCode.OK, RefusalBody()));

    [Fact]
    public async Task TruncatedOutput_IsAFailure() =>
        Assert.Equal(LlmErrorKind.Truncated, await FailureKindAsync(HttpStatusCode.OK, TruncatedBody()));

    [Fact]
    public async Task EmptyText_IsMalformed() =>
        Assert.Equal(LlmErrorKind.MalformedResponse, await FailureKindAsync(HttpStatusCode.OK, SuccessBody("")));

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, LlmErrorKind.PermissionDenied)]
    [InlineData(HttpStatusCode.NotFound, LlmErrorKind.ModelNotFound)]
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, LlmErrorKind.ServiceUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, LlmErrorKind.ServiceUnavailable)]
    public async Task HttpErrors_AreMapped(HttpStatusCode status, LlmErrorKind expected) =>
        Assert.Equal(expected, await FailureKindAsync(status, ErrorBody("Something went wrong.")));

    [Fact]
    public async Task ErrorMessages_NeverContainTheApiKey()
    {
        var handler = new StubHttpHandler()
            .Respond(HttpStatusCode.Unauthorized, ErrorBody($"invalid x-api-key {ApiKey}"))
            .Respond(HttpStatusCode.BadRequest, ErrorBody($"echo {ApiKey}"));
        var provider = CreateProvider(handler);

        var auth = await Assert.ThrowsAsync<LlmProviderException>(() => provider.GenerateAsync(Request, Ct));
        var invalid = await Assert.ThrowsAsync<LlmProviderException>(() => provider.GenerateAsync(Request, Ct));

        Assert.DoesNotContain(ApiKey, auth.Message, StringComparison.Ordinal);
        Assert.Contains(ApiKeyVariable, auth.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NetworkFailure_IsMapped()
    {
        var handler = new StubHttpHandler().Throw(new HttpRequestException("No such host is known."));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));

        Assert.Equal(LlmErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Cancellation_Propagates_AsCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new StubHttpHandler().Respond(HttpStatusCode.OK, SuccessBody("{}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProvider(handler).GenerateAsync(Request, cancelled.Token));
    }

    private async Task<LlmErrorKind> FailureKindAsync(HttpStatusCode status, string body)
    {
        var handler = new StubHttpHandler().Respond(status, body);
        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => CreateProvider(handler).GenerateAsync(Request, Ct));
        return ex.Kind;
    }
}

public sealed class OpenAiProviderContractTests : LlmProviderContractTests
{
    protected override string ApiKeyVariable => OpenAiProvider.ApiKeyVariable;

    private protected override ILlmProvider CreateProvider(StubHttpHandler handler) =>
        new OpenAiProvider(ApiKey, options: new OpenAiProviderOptions { HttpClient = new HttpClient(handler), MaxRetries = 0 });

    protected override string SuccessBody(string text) => OpenAiProviderTests.Completion(text, model: "served-model");

    protected override string RefusalBody() => OpenAiProviderTests.Completion(content: null, refusal: "I can't help with that.");

    protected override string TruncatedBody() => OpenAiProviderTests.Completion("""{"findings":[""", finishReason: "length");

    protected override string ErrorBody(string message) => OpenAiProviderTests.Error(message);
}

public sealed class AnthropicProviderContractTests : LlmProviderContractTests
{
    protected override string ApiKeyVariable => AnthropicProvider.ApiKeyVariable;

    private protected override ILlmProvider CreateProvider(StubHttpHandler handler) =>
        new AnthropicProvider(ApiKey, options: new AnthropicProviderOptions { HttpClient = new HttpClient(handler), MaxRetries = 0 });

    protected override string SuccessBody(string text) => AnthropicProviderTests.Message(text, model: "served-model");

    protected override string RefusalBody() => AnthropicProviderTests.Message(text: null, stopReason: "refusal");

    protected override string TruncatedBody() => AnthropicProviderTests.Message("""{"findings":[""", stopReason: "max_tokens");

    protected override string ErrorBody(string message) => AnthropicProviderTests.Error(message);
}

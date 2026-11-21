using System.ClientModel;
using System.ClientModel.Primitives;

using OpenAI;
using OpenAI.Chat;

using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;

namespace RibatIQ.Infrastructure.Llm;

/// <summary>Options for <see cref="OpenAiProvider"/>, mainly for tests and proxies.</summary>
public sealed record OpenAiProviderOptions
{
    /// <summary>API base address; <see langword="null"/> for the official endpoint.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>Per-attempt network timeout.</summary>
    public TimeSpan NetworkTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Retries for transient failures (408, 429, 5xx); <see langword="null"/> keeps the SDK default.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>HTTP client to send requests with; <see langword="null"/> uses the SDK's own.</summary>
    public HttpClient? HttpClient { get; init; }
}

/// <summary>
/// OpenAI adapter built on the official <c>OpenAI</c> .NET library (Chat Completions with strict
/// JSON-schema structured outputs). SDK types never leave this class.
/// </summary>
public sealed class OpenAiProvider : ILlmProvider
{
    public const string ApiKeyVariable = "OPENAI_API_KEY";
    public const string DefaultModel = "gpt-6.2-astra";
    private const string DisplayName = "OpenAI";

    private readonly ChatClient _client;
    private readonly string _apiKey;

    public OpenAiProvider(string apiKey, string? model = null, OpenAiProviderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        options ??= new OpenAiProviderOptions();

        _apiKey = apiKey;
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        var clientOptions = new OpenAIClientOptions { NetworkTimeout = options.NetworkTimeout };
        if (options.Endpoint is not null)
        {
            clientOptions.Endpoint = options.Endpoint;
        }

        if (options.MaxRetries is { } retries)
        {
            clientOptions.RetryPolicy = new ClientRetryPolicy(retries);
        }

        if (options.HttpClient is not null)
        {
            clientOptions.Transport = new HttpClientPipelineTransport(options.HttpClient);
        }

        _client = new ChatClient(Model, new ApiKeyCredential(apiKey), clientOptions);
    }

    public string ProviderId => LlmProviders.OpenAi;

    public string Model { get; }

    /// <summary>Creates the provider with the key from <see cref="ApiKeyVariable"/>.</summary>
    /// <exception cref="ConfigurationException">The key is not set.</exception>
    public static OpenAiProvider FromEnvironment(Func<string, string?> getEnvironmentVariable, string? model, OpenAiProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var key = getEnvironmentVariable(ApiKeyVariable);
        return string.IsNullOrWhiteSpace(key)
            ? throw new ConfigurationException($"{ApiKeyVariable} is not set. Set it to use the OpenAI provider, or run with --no-ai.")
            : new OpenAiProvider(key.Trim(), model, options);
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChatMessage[] messages =
        [
            new SystemChatMessage(request.SystemPrompt),
            new UserChatMessage(request.UserPrompt),
        ];

        var completionOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: request.ResponseSchema.Name,
                jsonSchema: BinaryData.FromString(request.ResponseSchema.SchemaJson),
                jsonSchemaIsStrict: true),
            MaxOutputTokenCount = request.MaxOutputTokens,
        };

        ChatCompletion completion;
        try
        {
            ClientResult<ChatCompletion> result = await _client.CompleteChatAsync(messages, completionOptions, cancellationToken)
                .ConfigureAwait(false);
            completion = result.Value;
        }
        catch (ClientResultException ex)
        {
            throw Error(ProviderErrors.FromStatus(ex.Status), ex.Message, ex, ex.Status);
        }
        catch (HttpRequestException ex)
        {
            throw Error(LlmErrorKind.Network, null, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Error(LlmErrorKind.Timeout, null, ex);
        }

        if (!string.IsNullOrEmpty(completion.Refusal) || completion.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw Error(LlmErrorKind.Refused, completion.Refusal, null);
        }

        if (completion.FinishReason == ChatFinishReason.Length)
        {
            throw Error(LlmErrorKind.Truncated, null, null);
        }

        var text = string.Concat(completion.Content.Select(part => part.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Error(LlmErrorKind.MalformedResponse, "The response contained no text.", null);
        }

        var usage = completion.Usage is { } u ? new LlmUsage(u.InputTokenCount, u.OutputTokenCount) : null;
        return new LlmResponse(text, string.IsNullOrEmpty(completion.Model) ? Model : completion.Model, usage);
    }

    private LlmProviderException Error(LlmErrorKind kind, string? detail, Exception? inner, int? status = null) =>
        ProviderErrors.Create(DisplayName, kind, ApiKeyVariable, Model, detail, _apiKey, inner, status);
}

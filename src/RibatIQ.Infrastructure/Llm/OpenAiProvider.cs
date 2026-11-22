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
    /// <summary>API base address; <see langword="null"/> for the service's own endpoint.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>Per-attempt network timeout.</summary>
    public TimeSpan NetworkTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Retries for transient failures (408, 429, 5xx); <see langword="null"/> keeps the SDK default.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>HTTP client to send requests with; <see langword="null"/> uses the SDK's own.</summary>
    public HttpClient? HttpClient { get; init; }
}

/// <summary>
/// Adapter for OpenAI and OpenAI-compatible services (see <see cref="OpenAiCompatibleService"/>), built on
/// the official <c>OpenAI</c> .NET library: Chat Completions with strict JSON-schema structured outputs.
/// SDK types never leave this class.
/// </summary>
public sealed class OpenAiProvider : ILlmProvider
{
    public const string ApiKeyVariable = "OPENAI_API_KEY";
    public const string DefaultModel = "gpt-6.2-astra";

    private readonly ChatClient _client;
    private readonly string _apiKey;
    private readonly OpenAiCompatibleService _service;

    // Set once the service has rejected a JSON schema, so later requests skip the failing attempt.
    private volatile bool _useJsonObject;

    public OpenAiProvider(string apiKey, string? model = null, OpenAiProviderOptions? options = null, OpenAiCompatibleService? service = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        options ??= new OpenAiProviderOptions();

        _apiKey = apiKey;
        _service = service ?? OpenAiCompatibleService.OpenAi;
        Model = string.IsNullOrWhiteSpace(model) ? _service.DefaultModel : model.Trim();

        var clientOptions = new OpenAIClientOptions { NetworkTimeout = options.NetworkTimeout };
        if ((options.Endpoint ?? _service.Endpoint) is { } endpoint)
        {
            clientOptions.Endpoint = endpoint;
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

    public string ProviderId => _service.ProviderId;

    public string Model { get; }

    /// <summary>Creates the provider with the key from the service's API key variable.</summary>
    /// <exception cref="ConfigurationException">The key is not set.</exception>
    public static OpenAiProvider FromEnvironment(
        Func<string, string?> getEnvironmentVariable, string? model, OpenAiProviderOptions? options = null, OpenAiCompatibleService? service = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        service ??= OpenAiCompatibleService.OpenAi;

        var key = getEnvironmentVariable(service.ApiKeyVariable);
        return string.IsNullOrWhiteSpace(key)
            ? throw new ConfigurationException(
                $"{service.ApiKeyVariable} is not set. Set it to use the {service.DisplayName} provider, or run with --no-ai.")
            : new OpenAiProvider(key.Trim(), model, options, service);
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChatCompletion completion;
        try
        {
            completion = await CompleteAsync(request, jsonObject: _useJsonObject, cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException ex) when (ex.Status == 400 && _service.JsonObjectFallback && !_useJsonObject)
        {
            // The service may not support strict JSON schemas: ask once more in plain JSON mode.
            _useJsonObject = true;
            completion = await CompleteAsync(request, jsonObject: true, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// One chat completion. In JSON-object mode the schema cannot be enforced by the service, so it is
    /// appended to the system prompt; the response is validated afterwards either way.
    /// </summary>
    /// <exception cref="ClientResultException">HTTP 400, when a JSON-object retry may still succeed.</exception>
    /// <exception cref="LlmProviderException">Any other failure.</exception>
    private async Task<ChatCompletion> CompleteAsync(LlmRequest request, bool jsonObject, CancellationToken cancellationToken)
    {
        var systemPrompt = jsonObject
            ? $"{request.SystemPrompt}\n\nReturn one JSON object that is valid against this JSON schema:\n{request.ResponseSchema.SchemaJson}"
            : request.SystemPrompt;

        ChatMessage[] messages =
        [
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(request.UserPrompt),
        ];

        var completionOptions = new ChatCompletionOptions
        {
            ResponseFormat = jsonObject
                ? ChatResponseFormat.CreateJsonObjectFormat()
                : ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: request.ResponseSchema.Name,
                    jsonSchema: BinaryData.FromString(request.ResponseSchema.SchemaJson),
                    jsonSchemaIsStrict: true),
            MaxOutputTokenCount = request.MaxOutputTokens,
        };

        try
        {
            ClientResult<ChatCompletion> result = await _client.CompleteChatAsync(messages, completionOptions, cancellationToken)
                .ConfigureAwait(false);
            return result.Value;
        }
        catch (ClientResultException ex) when (ex.Status == 400 && _service.JsonObjectFallback && !jsonObject)
        {
            throw;
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
    }

    private LlmProviderException Error(LlmErrorKind kind, string? detail, Exception? inner, int? status = null) =>
        ProviderErrors.Create(_service.DisplayName, kind, _service.ApiKeyVariable, Model, detail, _apiKey, inner, status);
}

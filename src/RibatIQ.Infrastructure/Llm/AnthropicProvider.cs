using System.Text.Json;

using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

using RibatIQ.Application.Ai;
using RibatIQ.Application.Configuration;

namespace RibatIQ.Infrastructure.Llm;

/// <summary>Options for <see cref="AnthropicProvider"/>, mainly for tests and proxies.</summary>
public sealed record AnthropicProviderOptions
{
    public const string OfficialBaseUrl = "https://api.anthropic.com";

    /// <summary>API base address; <see langword="null"/> for <see cref="OfficialBaseUrl"/>. Environment variables are not consulted.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Per-request timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Retries for transient failures (429, 5xx, connection errors); <see langword="null"/> keeps the SDK default.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>HTTP client to send requests with; <see langword="null"/> uses the SDK's own.</summary>
    public HttpClient? HttpClient { get; init; }
}

/// <summary>
/// Anthropic adapter built on the official <c>Anthropic</c> C# SDK (Messages API with JSON-schema
/// structured outputs). SDK types never leave this class.
/// </summary>
/// <remarks>
/// For models that support it, a server-side refusal fallback is requested so that a request the
/// model declines is re-served by a fallback model within the same call; the reported model is the
/// one that actually answered.
/// </remarks>
public sealed class AnthropicProvider : ILlmProvider, IDisposable
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string DefaultModel = "claude-opus-5-5";
    private const string DisplayName = "Anthropic";

    // Models that accept the server-side refusal fallback ("fallbacks": "default").
    private static readonly HashSet<string> RefusalFallbackModels = new(StringComparer.Ordinal)
    {
        "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5", "claude-fable-5-1", "claude-fable-5-2",
    };

    private readonly AnthropicClient _client;
    private readonly string _apiKey;

    public AnthropicProvider(string apiKey, string? model = null, AnthropicProviderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        options ??= new AnthropicProviderOptions();

        _apiKey = apiKey;
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        // Pin the endpoint and the credential: where source code is sent must not change because of
        // ambient variables (such as ANTHROPIC_BASE_URL) or profiles RibatIQ does not control.
        _client = new AnthropicClient
        {
            ApiKey = apiKey,
            BaseUrl = options.BaseUrl ?? AnthropicProviderOptions.OfficialBaseUrl,
            Timeout = options.Timeout,
        };

        if (options.MaxRetries is { } retries)
        {
            _client = (AnthropicClient)_client.WithOptions(o => o with { MaxRetries = retries });
        }

        if (options.HttpClient is not null)
        {
            _client = (AnthropicClient)_client.WithOptions(o => o with { HttpClient = options.HttpClient });
        }

    }

    public string ProviderId => LlmProviders.Anthropic;

    public string Model { get; }

    /// <summary>Whether requests ask for the server-side refusal fallback.</summary>
    public bool UsesRefusalFallback => RefusalFallbackModels.Contains(Model);

    /// <summary>Creates the provider with the key from <see cref="ApiKeyVariable"/>.</summary>
    /// <exception cref="ConfigurationException">The key is not set.</exception>
    public static AnthropicProvider FromEnvironment(Func<string, string?> getEnvironmentVariable, string? model, AnthropicProviderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var key = getEnvironmentVariable(ApiKeyVariable);
        return string.IsNullOrWhiteSpace(key)
            ? throw new ConfigurationException($"{ApiKeyVariable} is not set. Set it to use the Anthropic provider, or run with --no-ai.")
            : new AnthropicProvider(key.Trim(), model, options);
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = request.MaxOutputTokens,
            System = request.SystemPrompt,
            Messages = [new BetaUserMessageParam { Content = request.UserPrompt }],
            OutputConfig = new BetaOutputConfig
            {
                // Code review is reasoning-heavy; do not rely on lower model defaults.
                Effort = Effort.High,
                Format = new BetaJsonOutputFormat
                {
                    Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.ResponseSchema.SchemaJson)!,
                },
            },
        };

        if (UsesRefusalFallback)
        {
            parameters = parameters with
            {
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
            };
        }

        BetaMessage message;
        try
        {
            message = await _client.Beta.Messages.Create(parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw Error(LlmErrorKind.Authentication, null, ex, 401);
        }
        catch (AnthropicForbiddenException ex)
        {
            throw Error(LlmErrorKind.PermissionDenied, ex.ApiMessage, ex, 403);
        }
        catch (AnthropicNotFoundException ex)
        {
            throw Error(LlmErrorKind.ModelNotFound, ex.ApiMessage, ex, 404);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw Error(LlmErrorKind.RateLimited, ex.ApiMessage, ex, 429);
        }
        catch (Anthropic5xxException ex)
        {
            throw Error(LlmErrorKind.ServiceUnavailable, ex.ApiMessage, ex);
        }
        catch (AnthropicBadRequestException ex)
        {
            throw Error(LlmErrorKind.InvalidRequest, ex.ApiMessage, ex, 400);
        }
        catch (AnthropicApiException ex)
        {
            throw Error(LlmErrorKind.InvalidRequest, ex.ApiMessage, ex);
        }
        catch (AnthropicIOException ex)
        {
            throw Error(LlmErrorKind.Network, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Error(LlmErrorKind.Network, null, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Error(LlmErrorKind.Timeout, null, ex);
        }

        // ApiEnum.ToString() returns JSON (quoted); Raw() returns the value, including values unknown to the SDK.
        var stopReason = message.StopReason?.Raw();
        if (stopReason == "refusal")
        {
            throw Error(LlmErrorKind.Refused, message.StopDetails?.Explanation, null);
        }

        if (stopReason == "max_tokens")
        {
            throw Error(LlmErrorKind.Truncated, null, null);
        }

        var text = string.Concat(message.Content
            .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : string.Empty));

        if (string.IsNullOrWhiteSpace(text))
        {
            throw Error(LlmErrorKind.MalformedResponse, "The response contained no text.", null);
        }

        var usage = message.Usage is { } u ? new LlmUsage(u.InputTokens, u.OutputTokens) : null;
        var servedModel = message.Model.Raw();
        return new LlmResponse(text, string.IsNullOrEmpty(servedModel) ? Model : servedModel, usage);
    }

    public void Dispose() => _client.Dispose();

    private LlmProviderException Error(LlmErrorKind kind, string? detail, Exception? inner, int? status = null) =>
        ProviderErrors.Create(DisplayName, kind, ApiKeyVariable, Model, detail, _apiKey, inner, status);
}

using RibatIQ.Application.Ai;

namespace RibatIQ.Infrastructure.Llm;

/// <summary>Builds user-facing provider errors that never leak credentials.</summary>
internal static class ProviderErrors
{
    private const int MaxDetailLength = 300;

    /// <summary>Maps an HTTP status to an error kind shared by all providers.</summary>
    public static LlmErrorKind FromStatus(int status) => status switch
    {
        401 => LlmErrorKind.Authentication,
        402 or 403 => LlmErrorKind.PermissionDenied,
        404 => LlmErrorKind.ModelNotFound,
        408 => LlmErrorKind.Timeout,
        429 => LlmErrorKind.RateLimited,
        >= 500 => LlmErrorKind.ServiceUnavailable,
        0 => LlmErrorKind.Network,
        _ => LlmErrorKind.InvalidRequest,
    };

    public static LlmProviderException Create(
        string providerName, LlmErrorKind kind, string apiKeyVariable, string model, string? detail, string apiKey, Exception? inner, int? status = null)
    {
        var code = status is { } s and > 0 ? $" (HTTP {s})" : string.Empty;
        var message = kind switch
        {
            LlmErrorKind.Authentication => $"{providerName} rejected the API key{code}. Check {apiKeyVariable}.",
            LlmErrorKind.PermissionDenied => $"{providerName} denied access{code}. Check the account's permissions and billing.",
            LlmErrorKind.ModelNotFound => $"{providerName} does not know the model '{model}' or it is not available to this account{code}.",
            LlmErrorKind.RateLimited => $"{providerName} rate or spend limit reached{code}; retries were exhausted. Try again later.",
            LlmErrorKind.ServiceUnavailable => $"{providerName} is unavailable or overloaded{code}; retries were exhausted. Try again later.",
            LlmErrorKind.Timeout => $"{providerName} did not respond in time{code}.",
            LlmErrorKind.Network => $"{providerName} could not be reached. Check the network connection.",
            LlmErrorKind.Refused => $"{providerName} declined to analyze the code.",
            LlmErrorKind.Truncated => $"{providerName} stopped before the response was complete (output limit reached).",
            LlmErrorKind.InvalidRequest => $"{providerName} rejected the request{code}.",
            _ => $"{providerName} returned an unusable response.",
        };

        // Authentication details may echo parts of the key; never include them.
        if (kind != LlmErrorKind.Authentication && Sanitize(detail, apiKey) is { Length: > 0 } safe)
        {
            message += $" Details: {safe}";
        }

        return new LlmProviderException(kind, message, inner);
    }

    /// <summary>Removes the API key, line breaks and excess length from provider-supplied text.</summary>
    public static string? Sanitize(string? text, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var safe = text.ReplaceLineEndings(" ").Trim();
        if (!string.IsNullOrEmpty(apiKey))
        {
            safe = safe.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
        }

        return safe.Length <= MaxDetailLength ? safe : safe[..MaxDetailLength] + "...";
    }
}

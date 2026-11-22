namespace RibatIQ.Application.Ai;

/// <summary>Why an LLM request failed.</summary>
public enum LlmErrorKind
{
    /// <summary>The API key is invalid or revoked.</summary>
    Authentication,

    /// <summary>The credential may not use this model or operation, or billing is blocked.</summary>
    PermissionDenied,

    /// <summary>The model does not exist or is not available to the account.</summary>
    ModelNotFound,

    /// <summary>Rate or spend limits were hit and retries were exhausted.</summary>
    RateLimited,

    /// <summary>The service failed or was overloaded and retries were exhausted.</summary>
    ServiceUnavailable,

    /// <summary>The request was rejected as invalid (for example, too large).</summary>
    InvalidRequest,

    /// <summary>The request did not complete in time.</summary>
    Timeout,

    /// <summary>The provider could not be reached.</summary>
    Network,

    /// <summary>The model declined to answer.</summary>
    Refused,

    /// <summary>The output was cut off before it was complete.</summary>
    Truncated,

    /// <summary>The output does not match the expected format.</summary>
    MalformedResponse,
}

/// <summary>
/// An LLM request failed. Messages are user-facing and never contain API keys, prompts or source code.
/// </summary>
public sealed class LlmProviderException : Exception
{
    public LlmProviderException()
    {
    }

    public LlmProviderException(string message)
        : base(message)
    {
    }

    public LlmProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LlmProviderException(LlmErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public LlmErrorKind Kind { get; } = LlmErrorKind.MalformedResponse;
}

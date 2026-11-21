using RibatIQ.Application.Ai;

namespace RibatIQ.Application.Tests;

/// <summary>An in-memory provider that returns a scripted response and records the request.</summary>
internal sealed class FakeLlmProvider(string responseText, string providerId = "openai", string model = "fake-model") : ILlmProvider
{
    public string ProviderId => providerId;

    public string Model => model;

    public Exception? Failure { get; init; }

    public LlmUsage? Usage { get; init; } = new(1200, 80);

    public List<LlmRequest> Requests { get; } = [];

    public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Failure is not null
            ? Task.FromException<LlmResponse>(Failure)
            : Task.FromResult(new LlmResponse(responseText, model, Usage));
    }
}

/// <summary>Returns the same provider for every request and records what was asked.</summary>
internal sealed class FakeLlmProviderFactory(ILlmProvider provider) : ILlmProviderFactory
{
    public List<(string ProviderId, string? Model)> Created { get; } = [];

    public Exception? Failure { get; init; }

    public ILlmProvider Create(string providerId, string? model)
    {
        Created.Add((providerId, model));
        return Failure is not null ? throw Failure : provider;
    }
}

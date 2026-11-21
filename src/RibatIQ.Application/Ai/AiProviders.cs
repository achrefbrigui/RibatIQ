using RibatIQ.Application.Configuration;
using RibatIQ.Domain.Results;

namespace RibatIQ.Application.Ai;

/// <summary>Shared AI plumbing for the scan and review workflows.</summary>
internal static class AiProviders
{
    /// <summary>The provider to use, or <see langword="null"/> when AI analysis is disabled.</summary>
    public static ILlmProvider? Resolve(RibatSettings settings, ILlmProviderFactory? factory)
    {
        if (!settings.AiEnabled)
        {
            return null;
        }

        if (factory is null)
        {
            throw new OperationFailedException(
                $"AI analysis ({settings.Provider}) is not available in this build yet. " +
                "Run with --no-ai or remove the provider setting to run static analysis only.");
        }

        return factory.Create(settings.Provider!, settings.Model);
    }

    public static AnalysisMetadata Metadata(string toolVersion, AiAnalysisResult? ai) =>
        ai is null
            ? AnalysisMetadata.WithoutAi(toolVersion)
            : new AnalysisMetadata(toolVersion, aiEnabled: true, ai.ProviderId, ai.Model);
}

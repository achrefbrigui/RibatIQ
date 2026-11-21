using RibatIQ.Application.Configuration;

namespace RibatIQ.Cli.Configuration;

/// <summary>Reads non-secret settings from <c>RIBATIQ_*</c> environment variables.</summary>
internal static class EnvironmentSettings
{
    public const string ProviderVariable = "RIBATIQ_PROVIDER";
    public const string ModelVariable = "RIBATIQ_MODEL";
    public const string FailOnVariable = "RIBATIQ_FAIL_ON";

    public static SettingsLayer Read(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var failOnValue = NullIfBlank(getEnvironmentVariable(FailOnVariable));
        FailPolicy? failOn = null;
        if (failOnValue is not null)
        {
            if (!FailPolicy.TryParse(failOnValue, out var parsed))
            {
                throw new ConfigurationException(
                    $"Invalid {FailOnVariable} value '{failOnValue}'. Expected one of: {string.Join(", ", FailPolicy.AcceptedValues)}.");
            }

            failOn = parsed;
        }

        return new SettingsLayer("environment")
        {
            Provider = NullIfBlank(getEnvironmentVariable(ProviderVariable)),
            Model = NullIfBlank(getEnvironmentVariable(ModelVariable)),
            FailOn = failOn,
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

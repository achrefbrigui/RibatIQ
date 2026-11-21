namespace RibatIQ.Application.Configuration;

/// <summary>
/// Invalid or inconsistent configuration. Messages are user-facing, name the offending source
/// (command line, environment, configuration file) and never contain secret values.
/// </summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException()
    {
    }

    public ConfigurationException(string message)
        : base(message)
    {
    }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

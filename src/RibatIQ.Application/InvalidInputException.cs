namespace RibatIQ.Application;

/// <summary>
/// The user's input (target path, solution file, Git revision, ...) cannot be processed.
/// Reported with the invalid-usage exit code. Messages are user-facing.
/// </summary>
public sealed class InvalidInputException : Exception
{
    public InvalidInputException()
    {
    }

    public InvalidInputException(string message)
        : base(message)
    {
    }

    public InvalidInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

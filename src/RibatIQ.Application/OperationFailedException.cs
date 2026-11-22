namespace RibatIQ.Application;

/// <summary>
/// An external operation (Git, file system, LLM provider) failed. Reported with the
/// operational-failure exit code and never as a clean result. Messages are user-facing
/// and must not contain secrets or source code.
/// </summary>
public sealed class OperationFailedException : Exception
{
    public OperationFailedException()
    {
    }

    public OperationFailedException(string message)
        : base(message)
    {
    }

    public OperationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

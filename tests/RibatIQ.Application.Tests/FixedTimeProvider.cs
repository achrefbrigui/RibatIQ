namespace RibatIQ.Application.Tests;

/// <summary>A <see cref="TimeProvider"/> that always returns the same instant.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

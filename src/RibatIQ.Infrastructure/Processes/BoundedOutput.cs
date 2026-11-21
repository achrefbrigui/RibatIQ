using System.Text;

namespace RibatIQ.Infrastructure.Processes;

/// <summary>Reads a process stream up to a character limit, so a runaway tool cannot exhaust memory.</summary>
internal static class BoundedOutput
{
    private const int BufferSize = 16 * 1024;

    /// <summary>
    /// Reads <paramref name="reader"/> to the end, or until more than <paramref name="maxChars"/> characters
    /// were read. When the limit is exceeded, the rest of the stream is drained and discarded so the process
    /// can exit, and <see cref="Result.Exceeded"/> is <see langword="true"/>.
    /// </summary>
    public static async Task<Result> ReadAsync(TextReader reader, int maxChars, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);

        var text = new StringBuilder();
        var buffer = new char[BufferSize];
        var exceeded = false;

        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (exceeded)
            {
                continue;
            }

            if (text.Length + read > maxChars)
            {
                exceeded = true;
                text.Clear();
                continue;
            }

            text.Append(buffer, 0, read);
        }

        return new Result(exceeded ? string.Empty : text.ToString(), exceeded);
    }

    /// <param name="Text">The output, or empty when the limit was exceeded.</param>
    /// <param name="Exceeded">Whether the output was larger than the limit.</param>
    public sealed record Result(string Text, bool Exceeded);
}

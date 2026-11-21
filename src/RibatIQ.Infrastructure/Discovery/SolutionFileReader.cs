using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using RibatIQ.Application;

namespace RibatIQ.Infrastructure.Discovery;

/// <summary>Reads the C# project paths listed in a <c>.sln</c> or <c>.slnx</c> solution file.</summary>
internal static partial class SolutionFileReader
{
    /// <summary>Returns absolute paths of the <c>.csproj</c> files the solution references, in file order.</summary>
    /// <exception cref="InvalidInputException">The solution file cannot be parsed.</exception>
    public static async Task<IReadOnlyList<string>> ReadProjectPathsAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;

        var relativePaths = Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? await ReadSlnxAsync(solutionPath, cancellationToken).ConfigureAwait(false)
            : await ReadSlnAsync(solutionPath, cancellationToken).ConfigureAwait(false);

        return relativePaths
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFullPath(p.Replace('\\', Path.DirectorySeparatorChar), solutionDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IEnumerable<string>> ReadSlnAsync(string path, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines
            .Select(line => SlnProjectLine().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["path"].Value);
    }

    private static async Task<IEnumerable<string>> ReadSlnxAsync(string path, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = XmlReader.Create(stream, settings);
                var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);

                return document
                    .Descendants()
                    .Where(e => e.Name.LocalName == "Project")
                    .Select(e => (string?)e.Attribute("Path"))
                    .OfType<string>()
                    .ToArray();
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidInputException($"Solution file '{Path.GetFileName(path)}' is not valid XML (line {ex.LineNumber}).", ex);
        }
    }

    // Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Name", "src\Name\Name.csproj", "{GUID}"
    [GeneratedRegex("""^\s*Project\("\{[^}]*\}"\)\s*=\s*"(?<name>[^"]*)"\s*,\s*"(?<path>[^"]*)"\s*,""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SlnProjectLine();
}

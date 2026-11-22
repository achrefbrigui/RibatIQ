using System.CommandLine;

using RibatIQ.Application.Reporting;
using RibatIQ.Application.Scanning;
using RibatIQ.Cli.Output;
using RibatIQ.Infrastructure.Dependencies;
using RibatIQ.Infrastructure.Discovery;
using RibatIQ.Infrastructure.Instructions;
using RibatIQ.Infrastructure.Roslyn;

namespace RibatIQ.Cli.Commands;

/// <summary><c>ribat scan &lt;path&gt;</c>: analyze a solution, project or directory.</summary>
internal static class ScanCommand
{
    private static readonly string[] SupportedFileExtensions = [".sln", ".slnx", ".csproj"];

    public static Command Create(CliContext context)
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Solution (.sln, .slnx), project (.csproj) or directory to scan.",
        };

        var command = new Command(
            "scan",
            "Scan a .NET solution, project or directory with static analysis. Source files are only read, never modified.");
        command.Arguments.Add(pathArgument);

        var options = new AnalysisOptions(auditDependencies: true);
        options.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(pathArgument)!;

            if (!TryResolveTarget(context, path, out var request, out var error))
            {
                await context.Error.WriteLineAsync(error).ConfigureAwait(false);
                return ExitCodes.InvalidUsage;
            }

            return await options.RunWithSettingsAsync(
                parseResult,
                context,
                async settings =>
                {
                    var service = new ScanService(
                        new FileSystemSourceDiscovery(),
                        new RoslynStaticAnalyzer(),
                        TimeProvider.System,
                        ToolInfo.Version,
                        context.ResolveLlmProviders(),
                        instructions: new FileSystemInstructionsSource(),
                        dependencies: new DotnetListPackageAuditor());

                    var outcome = await service.RunAsync(request, settings, cancellationToken).ConfigureAwait(false);

                    var report = new AnalysisReport(
                        outcome.Result,
                        ReportPublisher.DisplayPath(request.TargetPath, context.CurrentDirectory),
                        outcome.Coverage,
                        new ReportStatistics(outcome.FileCount, ProjectCount: outcome.ProjectCount),
                        outcome.Ai,
                        settings.FailOn)
                    {
                        Policy = outcome.Policy,
                        PolicyExcluded = outcome.PolicyExcluded,
                        Instructions = outcome.Instructions,
                    };

                    return await options.PublishAsync(report, parseResult, context, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static bool TryResolveTarget(CliContext context, string path, out ScanRequest request, out string error)
    {
        request = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "The scan path must not be empty.";
            return false;
        }

        var fullPath = Path.GetFullPath(path, context.CurrentDirectory);

        if (Directory.Exists(fullPath))
        {
            request = new ScanRequest(fullPath);
            return true;
        }

        if (!File.Exists(fullPath))
        {
            error = $"The scan path '{path}' does not exist.";
            return false;
        }

        var extension = Path.GetExtension(fullPath);
        if (!SupportedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            error = $"Unsupported file '{path}'. Expected a directory or a {string.Join(", ", SupportedFileExtensions)} file.";
            return false;
        }

        request = new ScanRequest(fullPath);
        return true;
    }
}

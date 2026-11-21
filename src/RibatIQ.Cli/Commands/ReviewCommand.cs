using System.CommandLine;

using RibatIQ.Application.Reporting;
using RibatIQ.Application.Reviewing;
using RibatIQ.Cli.Output;
using RibatIQ.Infrastructure.Git;
using RibatIQ.Infrastructure.Instructions;
using RibatIQ.Infrastructure.Roslyn;

namespace RibatIQ.Cli.Commands;

/// <summary><c>ribat review --base &lt;ref&gt; [--head &lt;ref&gt;]</c>: review the changes between two Git revisions.</summary>
internal static class ReviewCommand
{
    public static Command Create(CliContext context)
    {
        var baseOption = new Option<string>("--base")
        {
            Description = "Git revision the changes are compared against, for example origin/main.",
            Required = true,
        };

        var headOption = new Option<string>("--head")
        {
            Description = "Git revision containing the changes. Files are read at this revision, not from the working tree.",
            DefaultValueFactory = _ => ReviewRequest.DefaultHeadRef,
        };

        var command = new Command(
            "review",
            "Review the C# changes between two Git revisions (since their merge base, like a pull request) " +
            "and report findings on changed lines. Run inside the repository.");

        AddRefValidator(baseOption);
        AddRefValidator(headOption);
        command.Options.Add(baseOption);
        command.Options.Add(headOption);

        var options = new AnalysisOptions();
        options.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = new ReviewRequest(parseResult.GetValue(baseOption)!, parseResult.GetValue(headOption)!);

            return await options.RunWithSettingsAsync(
                parseResult,
                context,
                async settings =>
                {
                    var service = new ReviewService(
                        new GitCli(),
                        new RoslynStaticAnalyzer(),
                        TimeProvider.System,
                        ToolInfo.Version,
                        llmProviders: context.ResolveLlmProviders(),
                        instructions: new FileSystemInstructionsSource());

                    var outcome = await service.RunAsync(request, settings, context.CurrentDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    var report = new AnalysisReport(
                        outcome.Result,
                        request.Range,
                        outcome.Coverage,
                        new ReportStatistics(
                            outcome.ReviewedFileCount,
                            ChangedFileCount: outcome.ChangedFileCount,
                            FindingsOutsideChanges: outcome.FindingsOutsideChanges),
                        outcome.Ai,
                        settings.FailOn);

                    return await options.PublishAsync(report, parseResult, context, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    private static void AddRefValidator(Option<string> option) =>
        option.Validators.Add(result =>
        {
            if (ReviewRequest.GetRefError(result.GetValueOrDefault<string>()) is { } error)
            {
                result.AddError($"{option.Name}: {error}");
            }
        });
}

using System.CommandLine;

using RibatIQ.Application.Reviewing;

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
            Description = "Git revision containing the changes.",
            DefaultValueFactory = _ => ReviewRequest.DefaultHeadRef,
        };

        var command = new Command("review", "Review the changes between two Git revisions.")
        {
            // Hidden until the review engine is wired in, so --help only lists working commands.
            Hidden = true,
        };
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
                _ => CommandResults.EngineNotAvailableAsync(context, "review", request.Range),
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

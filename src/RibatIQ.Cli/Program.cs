using RibatIQ.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Request graceful cancellation instead of killing the process.
    e.Cancel = true;
    cancellation.Cancel();
};

return await RibatCli.InvokeAsync(args, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false);

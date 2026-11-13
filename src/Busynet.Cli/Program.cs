using Busynet.Cli;
using Busynet.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Not disposed: Ctrl+C may still arrive while the process exits.
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // The first Ctrl+C stops the command cleanly (clearing the bar); a second one is left to kill the process.
    if (cts.IsCancellationRequested)
        return;
    e.Cancel = true;
    cts.Cancel();
};

return await BusynetApp.RunAsync(args, new ServiceCollection().AddBusynet(), Console.Out, Console.Error, cts.Token);

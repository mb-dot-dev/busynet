using System.CommandLine;
using System.Reactive.Concurrency;
using Busynet.Bar;
using Busynet.Bkk.Departures;
using Busynet.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Busynet.Cli.Features.BkkDepartures;

/// <summary>
/// <c>busynet bkk departures</c>: reads <c>BKK_API_KEY</c> (missing ends the command before the bar is touched),
/// then the watch, which clears, polls, rotates and draws until Ctrl+C. A rejected key or an unknown stop surfaces
/// on the first poll and ends the command with exit 1.
/// </summary>
public sealed class BkkDeparturesCommand(
    IBarFactory bars,
    IBkkDepartures departures,
    IScheduler scheduler,
    Func<string, string?> environment,
    TimeZoneInfo zone,
    ILogger<BkkDeparturesCommand> logger)
{
    public const string Description = "Rotate the next 3 departures from a BKK stop on the bar until Ctrl+C.";

    /// <summary>The <c>departures</c> command: parses the options, then runs <see cref="ExecuteAsync"/> from <paramref name="provider"/>.</summary>
    public static Command Define(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var options = new BkkDeparturesOptions(new BarOptions());
        var command = new Command("departures", Description);
        options.AddTo(command);
        command.SetAction((parse, ct) =>
        {
            var settings = options.Read(parse);
            // Before the command resolves: the logging filter and the error handler read it.
            provider.GetRequiredService<Verbosity>().IsVerbose = settings.Verbose;
            return provider.GetRequiredService<BkkDeparturesCommand>().ExecuteAsync(settings, ct);
        });
        return command;
    }

    public async Task<int> ExecuteAsync(BkkDeparturesSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var key = environment(BkkDeparturesSettings.ApiKeyVariable);
            if (string.IsNullOrWhiteSpace(key))
                throw new ConfigurationException(
                    $"{BkkDeparturesSettings.ApiKeyVariable} is not set. Get a key at https://opendata.bkk.hu.");
            key = key.Trim();

            var (_, bar) = BarConnector.Connect(bars, settings, environment);
            await BkkDeparturesWatch.RunAsync(
                ct => departures.GetDeparturesAsync(key, settings.StopId, ct),
                new DeparturesDisplay(bar),
                TimeSpan.FromSeconds(settings.Interval),
                TimeSpan.FromSeconds(settings.Rotate),
                zone,
                scheduler,
                logger,
                cancellationToken);
            return 0;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C, at any point, is the normal way to stop.
            return 0;
        }
    }
}
